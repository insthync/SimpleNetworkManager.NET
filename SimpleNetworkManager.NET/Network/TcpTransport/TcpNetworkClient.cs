using Cysharp.Threading.Tasks;
using Microsoft.Extensions.Logging;
using System;
using System.Net.Sockets;

namespace Insthync.SimpleNetworkManager.NET.Network.TcpTransport
{
    public class TcpNetworkClient : BaseNetworkClient, IDisposable
    {
        private bool _disposed;
        private TcpClient? _tcpClient;
        private TcpClientConnection? _clientConnection;

        public override BaseClientConnection? ClientConnection => _clientConnection;

        public TcpNetworkClient(ILoggerFactory loggerFactory) : base(loggerFactory)
        {
        }

        public override async UniTask ConnectAsync(string hostname, int port)
        {
            if (_clientConnection?.IsConnected ?? false)
            {
                _logger.LogWarning("Client is already connecting");
                return;
            }

            _tcpClient = new TcpClient();
            try
            {
                await _tcpClient.ConnectAsync(hostname, port);
                _clientConnection = new TcpClientConnection(_tcpClient, _loggerFactory.CreateLogger<TcpClientConnection>());
            }
            catch (Exception)
            {
                throw;
            }
        }

        public override async UniTask DisconnectAsync()
        {
            if (_clientConnection == null || !_clientConnection.IsConnected)
            {
                _logger.LogWarning("Client is not connecting");
                return;
            }
            await _clientConnection.DisconnectAsync();
            _clientConnection?.Dispose();
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;

            _clientConnection?.Dispose();
        }
    }
}
