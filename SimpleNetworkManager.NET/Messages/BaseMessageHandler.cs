using Cysharp.Threading.Tasks;
using Insthync.SimpleNetworkManager.NET.Network;
using System;

namespace Insthync.SimpleNetworkManager.NET.Messages
{
    public abstract class BaseMessageHandler<T> : IMessageHandler
        where T : BaseMessage
    {
        private static T? s_messageInstance;

        public virtual BaseMessage GetMessageInstance()
        {
            if (s_messageInstance == null)
                s_messageInstance = Activator.CreateInstance<T>();
            return s_messageInstance;
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
