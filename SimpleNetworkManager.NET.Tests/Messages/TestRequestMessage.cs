using Insthync.SimpleNetworkManager.NET.Messages;
using MessagePack;

namespace Insthync.SimpleNetworkManager.NET.Tests.Messages
{
    [MessagePackObject]
    public class TestRequestMessage : BaseRequestMessage
    {
        [Key(1)]
        public string? stringVal;

        public override uint GetMessageType()
        {
            return 2;
        }
    }
}
