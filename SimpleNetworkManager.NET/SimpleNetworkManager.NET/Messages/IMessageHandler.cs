using Cysharp.Threading.Tasks;

namespace Insthync.SimpleNetworkManager.NET.Messages
{
    public interface IMessageHandler
    {
        BaseMessage GetMessageInstance();
        public UniTask HandleDataAsync(object? data);
    }
}
