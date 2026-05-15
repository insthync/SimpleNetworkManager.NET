using Insthync.SimpleNetworkManager.NET.Messages.Error;
using Microsoft.Extensions.Logging;
using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Insthync.SimpleNetworkManager.NET.Network.WebSocketTransport
{
    public class WebSocketNetworkServer : BaseNetworkServer
    {
        private CancellationTokenSource? _cancellationTokenSource;
        private TcpListener? _tcpListener;
        private Task? _acceptConnectionsTask;
        private bool _isRunning;
        private int _runningPort;

        public override bool IsRunning => _isRunning;
        public override int RunningPort =>  _runningPort;

        public WebSocketNetworkServer(ILoggerFactory loggerFactory) : base(loggerFactory)
        {
        }

        public override async Task StartAsync(int port, CancellationToken cancellationToken)
        {
            if (_isRunning)
            {
                _logger.LogWarning("WebSocket server is already running on port {Port}", _runningPort);
                return;
            }

            try
            {
                _tcpListener = new TcpListener(IPAddress.Any, port);
                _tcpListener.Start();
                _runningPort = ((IPEndPoint)_tcpListener.LocalEndpoint).Port;
                _cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                _isRunning = true;
                _acceptConnectionsTask = AcceptConnectionsAsync(_cancellationTokenSource.Token);

                _logger.LogInformation("WebSocket server started successfully on port {Port}", _runningPort);
            }
            catch (Exception ex)
            {
                _runningPort = 0;
                _logger.LogError(ex, "Failed to start WebSocket server on port {Port}", port);
                await StopAsync();
                throw;
            }
        }

        public override async Task StopAsync()
        {
            if (!_isRunning)
            {
                _logger.LogInformation("WebSocket server is not running, nothing to stop");
                return;
            }

            try
            {
                _cancellationTokenSource?.Cancel();
                _tcpListener?.Stop();

                if (_acceptConnectionsTask != null)
                {
                    try
                    {
                        await _acceptConnectionsTask;
                    }
                    catch (OperationCanceledException)
                    {
                        _logger.LogDebug("WebSocket connection acceptance task cancelled as expected");
                    }
                }

                await _connectionManager.DisconnectAllClientsAsync();

                _isRunning = false;
                _runningPort = 0;
                _logger.LogInformation("WebSocket server stopped successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred while stopping WebSocket server");
                throw;
            }
            finally
            {
                _cancellationTokenSource?.Dispose();
                _cancellationTokenSource = null;
                _tcpListener = null;
                _acceptConnectionsTask = null;
            }
        }

        private async Task AcceptConnectionsAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested && _tcpListener != null)
            {
                try
                {
                    var tcpClient = await _tcpListener.AcceptTcpClientAsync();
                    _ = HandleAcceptedClientAsync(tcpClient, cancellationToken);
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (InvalidOperationException)
                {
                    break;
                }
                catch (SocketException ex)
                {
                    if (cancellationToken.IsCancellationRequested)
                        break;

                    _logger.LogWarning(ex, "Socket error while accepting WebSocket connections");
                    await Task.Delay(1000, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Unexpected error while accepting WebSocket connections");
                    await Task.Delay(1000, cancellationToken);
                }
            }
        }

        private async Task HandleAcceptedClientAsync(TcpClient tcpClient, CancellationToken cancellationToken)
        {
            WebSocketClientConnection? clientConnection = null;
            try
            {
                clientConnection = await WebSocketClientConnection.AcceptServerConnectionAsync(
                    tcpClient,
                    _loggerFactory.CreateLogger<WebSocketClientConnection>(),
                    cancellationToken);

                if (_connectionManager.ConnectionCount >= MaxConnections)
                {
                    await clientConnection.RejectConnectionAsync(ConnectionErrorTypes.CapacityRejection, "Server is at maximum capacity", false, 0);
                    clientConnection.Dispose();
                    return;
                }

                AddConnection(clientConnection);
                _ = clientConnection.HandleConnectionAsync(cancellationToken);

                _logger.LogInformation("WebSocket client connected: ConnectionId={ConnectionId}, TotalConnections={TotalConnections}",
                    clientConnection.ConnectionId,
                    _connectionManager.ConnectionCount);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to accept WebSocket client");
                clientConnection?.Dispose();
                tcpClient.Dispose();
            }
        }
    }
}
