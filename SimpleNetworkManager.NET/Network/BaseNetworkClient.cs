using Cysharp.Threading.Tasks;
using Insthync.SimpleNetworkManager.NET.Services;
using Microsoft.Extensions.Logging;
using System.Threading;

namespace Insthync.SimpleNetworkManager.NET.Network
{
    public abstract class BaseNetworkClient
    {
        protected readonly ILoggerFactory _loggerFactory;
        protected readonly ILogger<BaseNetworkClient> _logger;
        protected readonly MessageRouter _messageRouter;

        public MessageRouter MessageRouter => _messageRouter;
        public abstract BaseClientConnection? ClientConnection { get; }
        public bool IsConnected => ClientConnection?.IsConnected ?? false;

        public BaseNetworkClient(ILoggerFactory loggerFactory)
        {
            _loggerFactory = loggerFactory;
            _logger = _loggerFactory.CreateLogger<BaseNetworkClient>();
            _messageRouter = new MessageRouter(_loggerFactory.CreateLogger<MessageRouter>());
        }

        public abstract UniTask ConnectAsync(string hostname, int port, CancellationToken cancellationToken);
        public abstract UniTask DisconnectAsync();
    }
}
