using Cysharp.Threading.Tasks;
using Insthync.SimpleNetworkManager.NET.Network;
using System;

namespace Insthync.SimpleNetworkManager.NET.Messages
{
    public abstract class BaseRequestResponseMessageHandler<TRequest, TResponse> : BaseMessageHandler<TRequest>, IResponseMessageHandler
        where TRequest : BaseRequestMessage
        where TResponse : BaseResponseMessage
    {
        private static TResponse? s_responseMessageInstance;

        public virtual BaseMessage GetResponseMessageInstance()
        {
            if (s_responseMessageInstance == null)
                s_responseMessageInstance = Activator.CreateInstance<TResponse>();
            return s_responseMessageInstance;
        }

        protected override sealed async UniTask HandleAsync(BaseClientConnection clientConnection, TRequest data)
        {
            Guid requestId = data.RequestId;
            var response = await HandleRequestAsync(clientConnection, data);
            response.RequestId = requestId;
            await clientConnection.SendMessageAsync(data);
        }

        protected abstract UniTask<TResponse> HandleRequestAsync(BaseClientConnection clientConnection, TRequest request);

        public UniTask HandleResponseDataAsync(BaseClientConnection clientConnection, object? data)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));
            return HandleResponseAsync(clientConnection, (TResponse)data);
        }

        protected abstract UniTask HandleResponseAsync(BaseClientConnection clientConnection, TResponse request);
    }
}
