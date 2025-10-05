using Cysharp.Threading.Tasks;
using Insthync.SimpleNetworkManager.NET.Messages;
using Insthync.SimpleNetworkManager.NET.Network;

namespace Insthync.SimpleNetworkManager.NET.Tests.Messages
{
    public class TestMessageHandler : BaseMessageHandler<TestMessage>
    {
        public int intVal;
        public bool boolVal;
        public string? stringVal;

        protected override UniTask HandleAsync(BaseClientConnection clientConnection, TestMessage data)
        {
            intVal = data.intVal;
            boolVal = data.boolVal;
            stringVal = data.stringVal;
            return default;
        }
    }
}
