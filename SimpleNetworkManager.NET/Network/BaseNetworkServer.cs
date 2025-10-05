using Cysharp.Threading.Tasks;
using Insthync.SimpleNetworkManager.NET.Services;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;

namespace Insthync.SimpleNetworkManager.NET.Network
{
    public abstract class BaseNetworkServer
    {
        protected readonly ILoggerFactory _loggerFactory;
        protected readonly ILogger<BaseNetworkServer> _logger;
        protected readonly ConnectionManager _connectionManager;
        protected readonly MessageRouter _messageRouter;
        public int MaxConnections = 1;

        public ConnectionManager ConnectionManager => _connectionManager;
        public MessageRouter MessageRouter => _messageRouter;

        /// <summary>
        /// Indicates whether the server is currently running
        /// </summary>
        public abstract bool IsRunning { get; }

        public BaseNetworkServer(ILoggerFactory loggerFactory)
        {
            _loggerFactory = loggerFactory;
            _logger = _loggerFactory.CreateLogger<BaseNetworkServer>();
            _connectionManager = new ConnectionManager(_loggerFactory.CreateLogger<ConnectionManager>());
            _messageRouter = new MessageRouter(_loggerFactory.CreateLogger<MessageRouter>());
        }

        protected virtual void AddConnection(BaseClientConnection clientConnection)
        {
            // Subscribe to connection events
            clientConnection.Disconnected += OnClientDisconnected;
            clientConnection.MessageReceived += OnClientMessageReceived;

            // Add to connection manager
            _connectionManager.AddConnection(clientConnection);
        }

        /// <summary>
        /// Event handler for client disconnections
        /// </summary>
        /// <param name="clientConnection">The client connection that disconnected</param>
        protected virtual void OnClientDisconnected(BaseClientConnection clientConnection)
        {
            try
            {
                // Remove from connection manager
                _connectionManager.RemoveConnection(clientConnection.ConnectionId);

                // Unsubscribe from events
                clientConnection.Disconnected -= OnClientDisconnected;
                clientConnection.MessageReceived -= OnClientMessageReceived;

                _logger.LogInformation("Client disconnected: ConnectionId={ConnectionId}, RemainingConnections={RemainingConnections}",
                    clientConnection.ConnectionId,
                    _connectionManager.ConnectionCount);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error handling client disconnection for ConnectionId={ConnectionId}",
                    clientConnection.ConnectionId);
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
                _logger.LogDebug("Received message from client {ConnectionId}", clientConnection.ConnectionId);

                // Route the message to the appropriate handler
                await _messageRouter.RouteMessageAsync(clientConnection, message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing message from client {ConnectionId}", clientConnection.ConnectionId);
            }
        }

        /// <summary>
        /// Starts the server on the specified port
        /// </summary>
        /// <param name="port">Port to listen on</param>
        /// <param name="cancellationToken">Cancellation token for graceful shutdown</param>
        /// <returns>Task representing the async start operation</returns>
        public abstract UniTask StartAsync(int port, CancellationToken cancellationToken);

        /// <summary>
        /// Stops the server gracefully
        /// </summary>
        /// <returns>Task representing the async stop operation</returns>
        public abstract UniTask StopAsync();
    }
}
