using Insthync.SimpleNetworkManager.NET.Messages;
using Insthync.SimpleNetworkManager.NET.Network;
using MessagePack;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;

namespace Insthync.SimpleNetworkManager.NET.Services
{
    /// <summary>
    /// Routes incoming messages to appropriate message handlers based on message type.
    /// Provides thread-safe handler registration and async message routing.
    /// </summary>
    public class MessageRouterService
    {
        private readonly ILogger<MessageRouterService> _logger;
        private readonly ConcurrentDictionary<uint, IMessageHandler> _handlers;

        public MessageRouterService(ILogger<MessageRouterService> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _handlers = new ConcurrentDictionary<uint, IMessageHandler>();
        }

        public bool ContainsHandler(uint key)
        {
            return _handlers.ContainsKey(key);
        }

        /// <summary>
        /// Registers a message handler for a specific message type
        /// </summary>
        /// <typeparam name="T">Type of message the handler processes</typeparam>
        /// <param name="handler">Handler instance to register</param>
        /// <param name="dismissWarning">Dismiss warning message writing or not?</param>
        /// <exception cref="ArgumentNullException">Thrown when handler is null</exception>
        public void RegisterHandler<T>(BaseMessageHandler<T> handler, bool dismissWarning = false)
            where T : BaseMessage
        {
            if (handler == null)
                throw new ArgumentNullException(nameof(handler));

            // Get the message type from the generic type parameter
            var messageInstance = handler.GetMessageInstance();
            var messageType = messageInstance.GetMessageType();

            if (_handlers.TryAdd(messageType, handler))
            {
                _logger.LogDebug("Registered handler for message type {MessageType} ({TypeName})",
                    messageType, typeof(T).Name);
            }
            else
            {
                if (!dismissWarning)
                {
                    _logger.LogWarning("Handler for message type {MessageType} ({TypeName}) already registered, replacing",
                        messageType, typeof(T).Name);
                }
                _handlers[messageType] = handler;
            }
        }

        /// <summary>
        /// Routes a message to the appropriate handler
        /// </summary>
        /// <param name="clientConnection">Client connection that sent the message</param>
        /// <param name="buffer">Message buffer</param>
        /// <param name="length">Length of buffer</param>
        /// <returns>Task representing the async routing operation</returns>
        /// <exception cref="ArgumentNullException">Thrown when client or message is null</exception>
        public async Task RouteMessageAsync(BaseClientConnection clientConnection, byte[] buffer, int length)
        {
            if (clientConnection == null)
                throw new ArgumentNullException(nameof(clientConnection));
            if (buffer == null)
                throw new ArgumentNullException(nameof(buffer));

            byte[] data;
            uint messageType;
            try
            {
                data = BaseMessage.ExtractMessageData(buffer, length, out messageType);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to extract message header");
                await clientConnection.SendDeserializationErrorAsync(null);
                return;
            }

            if (!_handlers.TryGetValue(messageType, out var handler))
            {
                _logger.LogWarning("No handler registered for message type {MessageType}", messageType);
                if (!MessageTypes.IsProtocolErrorMessageType(messageType))
                    await clientConnection.SendUnknownMessageTypeErrorAsync(messageType);
                return;
            }

            var messageInstance = handler.GetMessageInstance();
            object? deserializedMessage;
            try
            {
                deserializedMessage = MessagePackSerializer.Deserialize(messageInstance.GetType(), data, messageInstance.GetMessagePackOptions());
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to deserialize message type {MessageType}", messageType);
                await clientConnection.SendDeserializationErrorAsync(messageType);
                return;
            }

            if (deserializedMessage == null)
            {
                _logger.LogWarning("Message type {MessageType} deserialized to null", messageType);
                await clientConnection.SendDeserializationErrorAsync(messageType);
                return;
            }

            await handler.HandleDataAsync(clientConnection, deserializedMessage);
        }
    }
}
