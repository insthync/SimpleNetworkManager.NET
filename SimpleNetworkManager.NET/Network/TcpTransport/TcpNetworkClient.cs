using Microsoft.Extensions.Logging;
using System;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Insthync.SimpleNetworkManager.NET.Network.TcpTransport
{
    public class TcpNetworkClient : BaseNetworkClient
    {
        private CancellationTokenSource? _cancellationTokenSource;
        private TcpClient? _tcpClient;
        private TcpClientConnection? _clientConnection;

        public override BaseClientConnection? ClientConnection => _clientConnection;

        public TcpNetworkClient(ILoggerFactory loggerFactory) : base(loggerFactory)
        {
        }

        public override async Task ConnectAsync(string hostname, int port, CancellationToken cancellationToken)
        {
            if (_clientConnection?.IsConnected ?? false)
            {
                _logger.LogWarning("Client is already connecting");
                return;
            }

            CleanupConnectionResources();

            try
            {
                // Create TCP client
                _tcpClient = new TcpClient();

                // Create cancellation token source for client operations
                _cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

                await ConnectWithCancellationAsync(_tcpClient, hostname, port, _cancellationTokenSource.Token);

                _clientConnection = new TcpClientConnection(_tcpClient, _loggerFactory.CreateLogger<TcpClientConnection>());

                // Setup connection (events, some initial values)
                SetupConnection();

                // Handle the connection asynchronously
                _ = _clientConnection.HandleConnectionAsync(_cancellationTokenSource.Token);

                _logger.LogInformation("Client connected: RemoteEndPoint={RemoteEndPoint}",
                    _clientConnection.TcpClient.Client.RemoteEndPoint);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                _logger.LogDebug("Connection cancelled");
                CleanupConnectionResources();
                throw;
            }
            catch (SocketException ex)
            {
                _logger.LogWarning(ex, "Socket error while making connection");
                CleanupConnectionResources();
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error while making connection");
                CleanupConnectionResources();
                throw;
            }
        }

        private static async Task ConnectWithCancellationAsync(TcpClient tcpClient, string hostname, int port, CancellationToken cancellationToken)
        {
            if (!cancellationToken.CanBeCanceled)
            {
                await tcpClient.ConnectAsync(hostname, port);
                return;
            }

            using (cancellationToken.Register(state => ((TcpClient)state!).Close(), tcpClient))
            {
                try
                {
                    await tcpClient.ConnectAsync(hostname, port);
                }
                catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
                {
                    throw new OperationCanceledException(cancellationToken);
                }
                catch (SocketException) when (cancellationToken.IsCancellationRequested)
                {
                    throw new OperationCanceledException(cancellationToken);
                }
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
                _logger.LogWarning(ex, "Error disposing previous client connection");
            }

            try
            {
                _tcpClient?.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error disposing TCP client");
            }

            try
            {
                _cancellationTokenSource?.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error disposing connection cancellation token source");
            }

            _clientConnection = null;
            _tcpClient = null;
            _cancellationTokenSource = null;
        }
    }
}
