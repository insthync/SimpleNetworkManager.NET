using Insthync.SimpleNetworkManager.NET.Network;
using System.Threading.Tasks;

namespace Insthync.SimpleNetworkManager.NET.Messages
{
    public class ResponseMessageHandler<T> : BaseMessageHandler<T>
        where T : BaseResponseMessage
    {
        protected override Task HandleAsync(BaseClientConnection clientConnection, T data)
        {
            clientConnection.Responded(data);
            return Task.CompletedTask;
        }
    }
}
