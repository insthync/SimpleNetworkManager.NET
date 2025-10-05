using Cysharp.Threading.Tasks;
using Insthync.SimpleNetworkManager.NET.Network;

namespace Insthync.SimpleNetworkManager.NET.Messages
{
    internal interface IResponseMessageHandler : IMessageHandler
    {
        BaseMessage GetResponseMessageInstance();
        public UniTask HandleResponseDataAsync(BaseClientConnection clientConnection, object? data);
    }
}
