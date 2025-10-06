using Insthync.SimpleNetworkManager.NET.Messages;
using MessagePack;

namespace Insthync.SimpleNetworkManager.NET.Tests.Messages
{
    [MessagePackObject]
    public class TestResponseMessage : BaseResponseMessage
    {
        [Key(3)]
        public string? stringVal;

        public override uint GetMessageType()
        {
            return 3;
        }
    }
}
