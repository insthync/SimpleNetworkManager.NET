using Cysharp.Threading.Tasks;
using Insthync.SimpleNetworkManager.NET.Network;

namespace Insthync.SimpleNetworkManager.NET.Messages
{
    public interface IMessageHandler
    {
        BaseMessage GetMessageInstance();
        public UniTask HandleDataAsync(BaseClientConnection clientConnection, object? data);
    }
}
