using Microsoft.Extensions.Logging;
using System;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;

namespace Insthync.SimpleNetworkManager.NET.Network.WebSocketTransport
{
    public class WebSocketNetworkClient : BaseNetworkClient
    {
        private CancellationTokenSource? _cancellationTokenSource;
        private ClientWebSocket? _clientWebSocket;
        private WebSocketClientConnection? _clientConnection;

        public override BaseClientConnection? ClientConnection => _clientConnection;
        public string Path { get; set; } = "/";
        public bool UseSecureConnection { get; set; }

        public WebSocketNetworkClient(ILoggerFactory loggerFactory) : base(loggerFactory)
        {
        }

        public override Task ConnectAsync(string hostname, int port, CancellationToken cancellationToken)
        {
            var scheme = UseSecureConnection ? "wss" : "ws";
            var path = string.IsNullOrEmpty(Path) ? "/" : Path;
            if (!path.StartsWith("/", StringComparison.Ordinal))
                path = "/" + path;

            var uri = new Uri($"{scheme}://{hostname}:{port}{path}");
            return ConnectAsync(uri, cancellationToken);
        }

        public async Task ConnectAsync(Uri uri, CancellationToken cancellationToken)
        {
            if (_clientConnection?.IsConnected ?? false)
            {
                _logger.LogWarning("WebSocket client is already connected");
                return;
            }

            CleanupConnectionResources();

            try
            {
                _clientWebSocket = new ClientWebSocket();
                _cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                await _clientWebSocket.ConnectAsync(uri, _cancellationTokenSource.Token);

                _clientConnection = new WebSocketClientConnection(_clientWebSocket, _loggerFactory.CreateLogger<WebSocketClientConnection>());
                SetupConnection();
                _ = _clientConnection.HandleConnectionAsync(_cancellationTokenSource.Token);

                _logger.LogInformation("WebSocket client connected to {Uri}", uri);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                _logger.LogDebug("WebSocket connection cancelled");
                CleanupConnectionResources();
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error while making WebSocket connection to {Uri}", uri);
                CleanupConnectionResources();
                throw;
            }
        }

        private void CleanupConnectionResources()
        {
            try
            {
                _clientConnection?.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error disposing previous WebSocket connection");
            }

            try
            {
                _clientWebSocket?.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error disposing ClientWebSocket");
            }

            try
            {
                _cancellationTokenSource?.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error disposing WebSocket cancellation token source");
            }

            _clientConnection = null;
            _clientWebSocket = null;
            _cancellationTokenSource = null;
        }
    }
}
