using Cysharp.Threading.Tasks;
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

        public UniTask HandleDataAsync(object? data)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));
            return HandleAsync((T)data);
        }

        protected abstract UniTask HandleAsync(T data);
    }
}
