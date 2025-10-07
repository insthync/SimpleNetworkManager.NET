using Cysharp.Threading.Tasks;
using Insthync.SimpleNetworkManager.NET.Network;
using System;

namespace Insthync.SimpleNetworkManager.NET.Messages
{
    public abstract class BaseMessageHandler<T> : IMessageHandler
        where T : BaseMessage
    {
        public BaseMessage GetMessageInstance()
        {
            return BaseMessage.GetDefaultInstance(typeof(T));
        }

        public UniTask HandleDataAsync(BaseClientConnection clientConnection, object? data)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));
            return HandleAsync(clientConnection, (T)data);
        }

        protected abstract UniTask HandleAsync(BaseClientConnection clientConnection, T data);
    }
}
