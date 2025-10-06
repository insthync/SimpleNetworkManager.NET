using Cysharp.Threading.Tasks;
using Insthync.SimpleNetworkManager.NET.Messages;
using Insthync.SimpleNetworkManager.NET.Network;

namespace Insthync.SimpleNetworkManager.NET.Tests.Messages
{
    public class TestRequestMessageHandler : BaseRequestMessageHandler<TestRequestMessage, TestResponseMessage>
    {
        protected override UniTask<TestResponseMessage> HandleRequestAsync(BaseClientConnection clientConnection, TestRequestMessage request)
        {
            return UniTask.FromResult(new TestResponseMessage()
            {
                Success = true,
                stringVal = request.stringVal + '_' + request.stringVal,
            });
        }
    }
}
