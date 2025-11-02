using Insthync.SimpleNetworkManager.NET.Network;
using System.Threading.Tasks;

namespace Insthync.SimpleNetworkManager.NET.Messages
{
    public interface IMessageHandler
    {
        BaseMessage GetMessageInstance();
        public Task HandleDataAsync(BaseClientConnection clientConnection, object? data);
    }
}
