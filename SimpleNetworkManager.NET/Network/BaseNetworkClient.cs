using Cysharp.Threading.Tasks;
using Insthync.SimpleNetworkManager.NET.Messages;
using Insthync.SimpleNetworkManager.NET.Services;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;

namespace Insthync.SimpleNetworkManager.NET.Network
{
    public abstract class BaseNetworkClient
    {
        protected readonly ILoggerFactory _loggerFactory;
        protected readonly ILogger<BaseNetworkClient> _logger;
        protected readonly MessageRouterService _messageRouterService;

        public MessageRouterService MessageRouterService => _messageRouterService;
        public abstract BaseClientConnection? ClientConnection { get; }
        public bool IsConnected => ClientConnection?.IsConnected ?? false;

        public BaseNetworkClient(ILoggerFactory loggerFactory)
        {
            _loggerFactory = loggerFactory;
            _logger = _loggerFactory.CreateLogger<BaseNetworkClient>();
            _messageRouterService = new MessageRouterService(_loggerFactory.CreateLogger<MessageRouterService>());
        }

        public async UniTask SendMessageAsync(BaseMessage message)
        {
            if (ClientConnection == null || !ClientConnection.IsConnected)
                throw new InvalidOperationException("Cannot send message: client is not connected.");
            await ClientConnection.SendMessageAsync(message);
        }

        public async UniTask DisconnectAsync()
        {
            if (ClientConnection == null || !ClientConnection.IsConnected)
                throw new InvalidOperationException("Cannot disconnect: client is not connected.");
            await ClientConnection.DisconnectAsync();
            ClientConnection.Dispose();
        }

        public async UniTask<TResponse?> SendRequestAsync<TResponse>(BaseRequestMessage request)
            where TResponse : BaseResponseMessage
        {
            if (ClientConnection == null || !ClientConnection.IsConnected)
                throw new InvalidOperationException("Cannot send request: client is not connected.");
            _messageRouterService.RegisterHandler(new ResponseMessageHandler<TResponse>(), true);
            return await ClientConnection.SendRequestAsync<TResponse>(request);
        }

        public void RegisterHandler<T>(BaseMessageHandler<T> handler)
            where T : BaseMessage
        {
            _messageRouterService.RegisterHandler(handler);
        }

        public void RegisterHandler<TRequest, TResponse>(BaseRequestMessageHandler<TRequest, TResponse> handler)
            where TRequest : BaseRequestMessage
            where TResponse : BaseResponseMessage
        {
            _messageRouterService.RegisterHandler(handler);
        }

        protected virtual void SetupConnection()
        {
            if (ClientConnection == null)
                return;
            // Subscribe to connection events
            ClientConnection.Disconnected += OnClientDisconnected;
            ClientConnection.MessageReceived += OnClientMessageReceived;
        }

        /// <summary>
        /// Event handler for client disconnections
        /// </summary>
        /// <param name="clientConnection">The client connection that disconnected</param>
        protected virtual void OnClientDisconnected(BaseClientConnection clientConnection)
        {
            try
            {
                // Unsubscribe from events
                clientConnection.Disconnected -= OnClientDisconnected;
                clientConnection.MessageReceived -= OnClientMessageReceived;

                _logger.LogInformation("Client disconnected from server");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error handling client disconnection from server");
            }
        }

        /// <summary>
        /// Event handler for messages received from clients
        /// </summary>
        /// <param name="clientConnection">The client connection that sent the message</param>
        /// <param name="message">Received message</param>
        protected virtual async void OnClientMessageReceived(BaseClientConnection clientConnection, byte[] message)
        {
            try
            {
                _logger.LogDebug("Received message from server");

                // Route the message to the appropriate handler
                await _messageRouterService.RouteMessageAsync(clientConnection, message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing message from server");
            }
        }

        public abstract UniTask ConnectAsync(string hostname, int port, CancellationToken cancellationToken);
    }
}
