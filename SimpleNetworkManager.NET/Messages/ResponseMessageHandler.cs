using Cysharp.Threading.Tasks;
using Insthync.SimpleNetworkManager.NET.Network;
using System;

namespace Insthync.SimpleNetworkManager.NET.Messages
{
    public class ResponseMessageHandler<T> : BaseMessageHandler<T>
        where T : BaseResponseMessage
    {
        protected override UniTask HandleAsync(BaseClientConnection clientConnection, T data)
        {
            clientConnection.Responded(data);
            return default;
        }
    }
}
