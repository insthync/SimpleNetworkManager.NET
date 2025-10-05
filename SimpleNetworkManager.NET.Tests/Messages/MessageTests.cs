using Insthync.SimpleNetworkManager.NET.Messages;
using MessagePack;

namespace Insthync.SimpleNetworkManager.NET.Tests.Messages
{
    public class MessageTests
    {
        [Fact]
        public void TestMessage_Serialize_Deserialize()
        {
            var originalMessage = new TestMessage()
            {
                intVal = 123,
                boolVal = true,
                stringVal = "Hello",
            };

            // Act - Serialize using custom binary format
            var serializedData = originalMessage.Serialize();

            // Extract message data and verify header
            var messageData = BaseMessage.ExtractMessageData(serializedData, out uint messageType);
            Assert.Equal(originalMessage.GetMessageType(), messageType);

            // Deserialize the message data using MessagePack
            TestMessage? deserializedMessage = (TestMessage?)MessagePackSerializer.Deserialize(originalMessage.GetType(), messageData, originalMessage.GetMessagePackOptions());

            // Assert
            Assert.Equal(originalMessage.intVal, deserializedMessage?.intVal);
            Assert.Equal(originalMessage.boolVal, deserializedMessage?.boolVal);
            Assert.Equal(originalMessage.stringVal, deserializedMessage?.stringVal);
        }
    }
}
