using Cysharp.Threading.Tasks;
using Insthync.SimpleNetworkManager.NET.Messages;
using Insthync.SimpleNetworkManager.NET.Network;
using MessagePack;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;

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
        public async UniTask RouteMessageAsync(BaseClientConnection clientConnection, byte[] buffer, int length)
        {
            if (clientConnection == null)
                throw new ArgumentNullException(nameof(clientConnection));
            if (buffer == null)
                throw new ArgumentNullException(nameof(buffer));

            var data = BaseMessage.ExtractMessageData(buffer, length, out var messageType);
            if (_handlers.TryGetValue(messageType, out var handler))
            {
                var messageInstance = handler.GetMessageInstance();
                var dataType = messageInstance.GetType();
                await handler.HandleDataAsync(clientConnection, MessagePackSerializer.Deserialize(messageInstance.GetType(), data, messageInstance.GetMessagePackOptions()));
            }
        }
    }
}
