using Insthync.SimpleNetworkManager.NET.Network;
using System;
using System.Threading.Tasks;

namespace Insthync.SimpleNetworkManager.NET.Messages
{
    public abstract class BaseRequestMessageHandler<TRequest, TResponse> : BaseMessageHandler<TRequest>
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

        protected override sealed async Task HandleAsync(BaseClientConnection clientConnection, TRequest data)
        {
            uint requestId = data.RequestId;
            var response = await HandleRequestAsync(clientConnection, data);
            response.RequestId = requestId;
            await clientConnection.SendMessageAsync(response);
        }

        protected abstract Task<TResponse> HandleRequestAsync(BaseClientConnection clientConnection, TRequest request);
    }
}
