using Insthync.SimpleNetworkManager.NET.Messages;
using MessagePack;

namespace Insthync.SimpleNetworkManager.NET.Tests.Messages
{
    [MessagePackObject]
    public class TestMessage : BaseMessage
    {
        public override uint GetMessageType()
        {
            return 1;
        }

        [Key(0)]
        public int intVal;

        [Key(1)]
        public bool boolVal;

        [Key(2)]
        public string? stringVal;
    }
}
