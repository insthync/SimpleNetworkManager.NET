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
        private readonly ConcurrentDictionary<uint, IMessageHandler> _requestHandlers;
        private readonly ConcurrentDictionary<uint, IResponseMessageHandler> _responseHandlers;

        public MessageRouterService(ILogger<MessageRouterService> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _handlers = new ConcurrentDictionary<uint, IMessageHandler>();
            _requestHandlers = new ConcurrentDictionary<uint, IMessageHandler>();
            _responseHandlers = new ConcurrentDictionary<uint, IResponseMessageHandler>();
        }

        /// <summary>
        /// Registers a message handler for a specific message type
        /// </summary>
        /// <typeparam name="T">Type of message the handler processes</typeparam>
        /// <param name="handler">Handler instance to register</param>
        /// <exception cref="ArgumentNullException">Thrown when handler is null</exception>
        public void RegisterHandler<T>(BaseMessageHandler<T> handler)
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
                _logger.LogWarning("Handler for message type {MessageType} ({TypeName}) already registered, replacing",
                    messageType, typeof(T).Name);
                _handlers[messageType] = handler;
            }
        }

        /// <summary>
        /// Registers a request message handler for a specific message type
        /// </summary>
        /// <typeparam name="TRequest">Type of request message the handler processes</typeparam>
        /// <typeparam name="TResponse">Type of response message the handler processes</typeparam>
        /// <param name="handler">Handler instance to register</param>
        /// <exception cref="ArgumentNullException">Thrown when handler is null</exception>
        public void RegisterHandler<TRequest, TResponse>(BaseRequestResponseMessageHandler<TRequest, TResponse> handler)
            where TRequest : BaseRequestMessage
            where TResponse : BaseResponseMessage
        {
            if (handler == null)
                throw new ArgumentNullException(nameof(handler));

            // Get the request message type from the generic type parameter
            var requestInstance = handler.GetMessageInstance();
            var requestType = requestInstance.GetMessageType();

            if (_requestHandlers.TryAdd(requestType, handler))
            {
                _logger.LogDebug("Registered handler for message type {MessageType} ({TypeName})",
                    requestType, typeof(TRequest).Name);
            }
            else
            {
                _logger.LogWarning("Handler for message type {MessageType} ({TypeName}) already registered, replacing",
                    requestType, typeof(TRequest).Name);
                _requestHandlers[requestType] = handler;
            }

            // Get the response message type from the generic type parameter
            var responseInstance = handler.GetResponseMessageInstance();
            var responseType = responseInstance.GetMessageType();

            if (_responseHandlers.TryAdd(responseType, handler))
            {
                _logger.LogDebug("Registered handler for message type {MessageType} ({TypeName})",
                    responseType, typeof(TResponse).Name);
            }
            else
            {
                _logger.LogWarning("Handler for message type {MessageType} ({TypeName}) already registered, replacing",
                    responseType, typeof(TResponse).Name);
                _responseHandlers[responseType] = handler;
            }
        }

        /// <summary>
        /// Routes a message to the appropriate handler
        /// </summary>
        /// <param name="clientConnection">Client connection that sent the message</param>
        /// <param name="message">Message to route</param>
        /// <returns>Task representing the async routing operation</returns>
        /// <exception cref="ArgumentNullException">Thrown when client or message is null</exception>
        public async UniTask RouteMessageAsync(BaseClientConnection clientConnection, byte[] message)
        {
            if (clientConnection == null)
                throw new ArgumentNullException(nameof(clientConnection));
            if (message == null)
                throw new ArgumentNullException(nameof(message));

            var data = BaseMessage.ExtractMessageData(message, out var messageType);
            if (_handlers.TryGetValue(messageType, out var handler))
            {
                var messageInstance = handler.GetMessageInstance();
                var dataType = messageInstance.GetType();
                await handler.HandleDataAsync(clientConnection, MessagePackSerializer.Deserialize(messageInstance.GetType(), data, messageInstance.GetMessagePackOptions()));
            }

            if (_requestHandlers.TryGetValue(messageType, out var requestHandler))
            {
                var messageInstance = requestHandler.GetMessageInstance();
                var dataType = messageInstance.GetType();
                await requestHandler.HandleDataAsync(clientConnection, MessagePackSerializer.Deserialize(messageInstance.GetType(), data, messageInstance.GetMessagePackOptions()));
            }

            if (_responseHandlers.TryGetValue(messageType, out var responseHandler))
            {
                var messageInstance = responseHandler.GetMessageInstance();
                var dataType = messageInstance.GetType();
                await responseHandler.HandleResponseDataAsync(clientConnection, MessagePackSerializer.Deserialize(messageInstance.GetType(), data, messageInstance.GetMessagePackOptions()));
            }
        }
    }
}
