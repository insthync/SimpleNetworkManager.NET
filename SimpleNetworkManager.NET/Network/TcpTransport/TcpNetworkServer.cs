using Cysharp.Threading.Tasks;
using Insthync.SimpleNetworkManager.NET.Messages.Error;
using Microsoft.Extensions.Logging;
using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Insthync.SimpleNetworkManager.NET.Network.TcpTransport
{
    public class TcpNetworkServer : BaseNetworkServer
    {
        private CancellationTokenSource? _cancellationTokenSource;
        private TcpListener? _tcpListener;
        private UniTask? _acceptConnectionsTask;
        private bool _isRunning;
        private int _runningPort;

        public override bool IsRunning => _isRunning;

        public TcpNetworkServer(ILoggerFactory loggerFactory) : base(loggerFactory)
        {
        }

        public override async UniTask StartAsync(int port, CancellationToken cancellationToken)
        {
            if (_isRunning)
            {
                _logger.LogWarning("Server is already running on port {Port}", _runningPort);
                return;
            }

            try
            {
                _logger.LogInformation("Starting TCP server on port {Port} with max connections {MaxConnections}",
                    port, MaxConnections);

                // Create TCP listener
                _tcpListener = new TcpListener(IPAddress.Any, port);
                _tcpListener.Start();

                // Create cancellation token source for server operations
                _cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

                _isRunning = true;

                // Start accepting connections
                _acceptConnectionsTask = AcceptConnectionsAsync(_cancellationTokenSource.Token);
                _runningPort = port;
                _logger.LogInformation("TCP server started successfully on port {Port}", port);
            }
            catch (Exception ex)
            {
                _runningPort = 0;
                _logger.LogError(ex, "Failed to start TCP server on port {Port}", port);
                await StopAsync();
                throw;
            }
        }

        /// <summary>
        /// Stops the TCP server gracefully
        /// </summary>
        /// <returns>Task representing the async stop operation</returns>
        public override async UniTask StopAsync()
        {
            if (!_isRunning)
            {
                _logger.LogInformation("Server is not running, nothing to stop");
                return;
            }

            _logger.LogInformation("Stopping TCP server...");

            try
            {
                // Signal cancellation to stop accepting new connections
                _cancellationTokenSource?.Cancel();

                // Stop the TCP listener
                _tcpListener?.Stop();

                // Wait for the accept connections task to complete
                if (_acceptConnectionsTask != null)
                {
                    try
                    {
                        await _acceptConnectionsTask.Value;
                    }
                    catch (OperationCanceledException)
                    {
                        // Expected when cancellation is requested
                        _logger.LogDebug("Connection acceptance task cancelled as expected");
                    }
                }

                // Disconnect all active connections
                await _connectionManager.DisconnectAllClientsAsync();

                _isRunning = false;
                _logger.LogInformation("TCP server stopped successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred while stopping TCP server");
                throw;
            }
            finally
            {
                // Clean up resources
                _cancellationTokenSource?.Dispose();
                _cancellationTokenSource = null;
                _tcpListener = null;
                _acceptConnectionsTask = null;
            }
        }

        /// <summary>
        /// Main loop for accepting incoming client connections
        /// </summary>
        /// <param name="cancellationToken">Cancellation token to stop accepting connections</param>
        /// <returns>Task representing the async operation</returns>
        private async UniTask AcceptConnectionsAsync(CancellationToken cancellationToken)
        {
            _logger.LogDebug("Started accepting connections");

            while (!cancellationToken.IsCancellationRequested && _tcpListener != null)
            {
                try
                {
                    // Accept new client connection first
                    var tcpClientConnection = new TcpClientConnection(await _tcpListener.AcceptTcpClientAsync(), _loggerFactory.CreateLogger<TcpClientConnection>());

                    _logger.LogDebug("Accepted new TCP client from {RemoteEndPoint}", tcpClientConnection.TcpClient.Client.RemoteEndPoint);

                    // Check connection limit after accepting
                    if (_connectionManager.ConnectionCount >= MaxConnections)
                    {
                        _logger.LogWarning("Server is at maximum capacity ({MaxConnections} connections), rejecting new connections",
                            MaxConnections);

                        // Reject the connection
                        tcpClientConnection.RejectConnectionAsync(ConnectionErrorTypes.CapacityRejection, "Server is at maximum capacity", false, 0).Forget();
                        continue;
                    }

                    // Add to connection manager
                    AddConnection(tcpClientConnection);

                    // Handle the new connection asynchronously
                    tcpClientConnection.HandleConnectionAsync(cancellationToken).Forget();

                    _logger.LogInformation("Client connected: ConnectionId={ConnectionId}, RemoteEndPoint={RemoteEndPoint}, TotalConnections={TotalConnections}",
                        tcpClientConnection.ConnectionId,
                        tcpClientConnection.TcpClient.Client.RemoteEndPoint,
                        _connectionManager.ConnectionCount);
                }
                catch (ObjectDisposedException)
                {
                    // TCP listener was disposed, likely during shutdown
                    _logger.LogDebug("TCP listener disposed, stopping connection acceptance");
                    break;
                }
                catch (InvalidOperationException)
                {
                    // TCP listener was stopped
                    _logger.LogDebug("TCP listener stopped, stopping connection acceptance");
                    break;
                }
                catch (SocketException ex)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        _logger.LogDebug("Connection acceptance cancelled");
                        break;
                    }

                    _logger.LogWarning(ex, "Socket error while accepting connections");

                    // Brief delay before retrying to avoid tight loop on persistent errors
                    await Task.Delay(1000, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Unexpected error while accepting connections");

                    // Brief delay before retrying
                    await Task.Delay(1000, cancellationToken);
                }
            }

            _logger.LogDebug("Stopped accepting connections");
        }
    }
}
