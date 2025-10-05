using Insthync.SimpleNetworkManager.NET.Messages;
using Insthync.SimpleNetworkManager.NET.Network;
using Insthync.SimpleNetworkManager.NET.Tests.Messages;
using MessagePack;

namespace Insthync.SimpleNetworkManager.NET.Tests.Network
{
    public class StreamExtensionsTests
    {
        [Fact]
        public async Task ReadMessage_Test()
        {
            using var stream = new MemoryStream();
            var originalMessage = new TestMessage()
            {
                intVal = 999,
                boolVal = true,
                stringVal = "Test",
            };
            var cancellationTokenSource = new CancellationTokenSource();
            await stream.WriteMessageAsync(originalMessage, cancellationTokenSource.Token);
            stream.Position = 0;
            var message = await stream.ReadMessageAsync(cancellationTokenSource.Token);
            Assert.NotNull(message);

            var messageData = BaseMessage.ExtractMessageData(message, out var messageType);
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
