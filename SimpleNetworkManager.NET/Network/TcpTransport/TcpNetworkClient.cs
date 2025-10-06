using Cysharp.Threading.Tasks;
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

        public override async UniTask ConnectAsync(string hostname, int port, CancellationToken cancellationToken)
        {
            if (_clientConnection?.IsConnected ?? false)
            {
                _logger.LogWarning("Client is already connecting");
                return;
            }

            try
            {
                // Create TCP client
                _tcpClient = new TcpClient();

                // Create cancellation token source for client operations
                _cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

                await _tcpClient.ConnectAsync(hostname, port);

                _clientConnection = new TcpClientConnection(_tcpClient, _loggerFactory.CreateLogger<TcpClientConnection>());

                // Setup connection (events, some initial values)
                SetupConnection();

                // Handle the connection asynchronously
                _clientConnection.HandleConnectionAsync(_cancellationTokenSource.Token).Forget();

                _logger.LogInformation("Client connected: RemoteEndPoint={RemoteEndPoint}",
                    _clientConnection.TcpClient.Client.RemoteEndPoint);
            }
            catch (ObjectDisposedException)
            {
                // TCP client was disposed, likely during shutdown
                _logger.LogDebug("TCP client disposed, stopping connection");
                return;
            }
            catch (SocketException ex)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    _logger.LogDebug("Connection cancelled");
                    return;
                }

                _logger.LogWarning(ex, "Socket error while making connection");

                // Brief delay before retrying to avoid tight loop on persistent errors
                await Task.Delay(1000, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error while making connection");

                // Brief delay before retrying
                await Task.Delay(1000, cancellationToken);
            }
        }
    }
}
