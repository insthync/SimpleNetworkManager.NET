using Insthync.SimpleNetworkManager.NET.Network.WebSocketTransport;
using Insthync.SimpleNetworkManager.NET.Tests.Messages;
using Microsoft.Extensions.Logging;
using Moq;

namespace Insthync.SimpleNetworkManager.NET.Tests.Network.WebSocketTransport
{
    public class WebSocketTransportTests
    {
        private readonly Mock<ILoggerFactory> _loggerFactoryMock;
        private readonly Mock<ILogger> _loggerMock;

        public WebSocketTransportTests()
        {
            _loggerMock = new Mock<ILogger>();
            _loggerFactoryMock = new Mock<ILoggerFactory>();

            _loggerFactoryMock
                .Setup(f => f.CreateLogger(It.IsAny<string>()))
                .Returns(_loggerMock.Object);
        }

        private async Task<WebSocketNetworkServer> StartServerAsync(int maxConnections = 1)
        {
            var server = new WebSocketNetworkServer(_loggerFactoryMock.Object)
            {
                MaxConnections = maxConnections
            };

            await server.StartAsync(0, CancellationToken.None);
            Assert.True(server.RunningPort > 0);
            return server;
        }

        private static async Task WaitUntilAsync(Func<bool> condition, int timeoutMs = 2_000)
        {
            var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (DateTime.UtcNow < deadline)
            {
                if (condition())
                    return;

                await Task.Delay(10);
            }

            Assert.True(condition(), "Condition was not met before the timeout.");
        }

        [Fact]
        public async Task TestSimpleConnection()
        {
            var server = await StartServerAsync();
            var client = new WebSocketNetworkClient(_loggerFactoryMock.Object);

            await client.ConnectAsync("127.0.0.1", server.RunningPort, CancellationToken.None);

            Assert.True(server.IsRunning);
            Assert.True(client.IsConnected);

            await client.DisconnectAsync();
            await server.StopAsync();

            Assert.False(server.IsRunning);
            Assert.False(client.IsConnected);
        }

        [Fact]
        public async Task TestMessageFromClientHandling()
        {
            var server = await StartServerAsync();
            var serverTestMsgHandler = new TestMessageHandler();
            server.RegisterHandler(serverTestMsgHandler);
            var client = new WebSocketNetworkClient(_loggerFactoryMock.Object);

            await client.ConnectAsync("127.0.0.1", server.RunningPort, CancellationToken.None);

            await client.SendMessageAsync(new TestMessage()
            {
                stringVal = "HelloWebSocketClient",
            });

            await WaitUntilAsync(() => serverTestMsgHandler.stringVal == "HelloWebSocketClient");
            Assert.Equal("HelloWebSocketClient", serverTestMsgHandler.stringVal);

            await client.DisconnectAsync();
            await server.StopAsync();
        }

        [Fact]
        public async Task TestMessageFromServerHandling()
        {
            var server = await StartServerAsync();
            var client = new WebSocketNetworkClient(_loggerFactoryMock.Object);
            var clientTestMsgHandler = new TestMessageHandler();
            client.RegisterHandler(clientTestMsgHandler);

            await client.ConnectAsync("127.0.0.1", server.RunningPort, CancellationToken.None);
            await WaitUntilAsync(() => server.ConnectionManager.ConnectionCount == 1);

            await server.SendMessageAsync(server.ConnectionManager.GetAllConnections().First().ConnectionId, new TestMessage()
            {
                stringVal = "HelloWebSocketServer",
            });

            await WaitUntilAsync(() => clientTestMsgHandler.stringVal == "HelloWebSocketServer");
            Assert.Equal("HelloWebSocketServer", clientTestMsgHandler.stringVal);

            await client.DisconnectAsync();
            await server.StopAsync();
        }

        [Fact]
        public async Task TestRequestResponse()
        {
            var server = await StartServerAsync();
            server.RegisterHandler(new TestRequestMessageHandler());
            var client = new WebSocketNetworkClient(_loggerFactoryMock.Object);

            await client.ConnectAsync("127.0.0.1", server.RunningPort, CancellationToken.None);

            var response = await client.SendRequestAsync<TestResponseMessage>(new TestRequestMessage()
            {
                stringVal = "HelloWebSocket",
            });

            Assert.Equal("HelloWebSocket_HelloWebSocket", response.stringVal);

            await client.DisconnectAsync();
            await server.StopAsync();
        }

        [Fact]
        public async Task TestClientMaxConnections()
        {
            var server = await StartServerAsync(maxConnections: 1);
            var client1 = new WebSocketNetworkClient(_loggerFactoryMock.Object);
            var client2 = new WebSocketNetworkClient(_loggerFactoryMock.Object);

            await client1.ConnectAsync("127.0.0.1", server.RunningPort, CancellationToken.None);
            await WaitUntilAsync(() => server.ConnectionManager.ConnectionCount == 1);

            await client2.ConnectAsync("127.0.0.1", server.RunningPort, CancellationToken.None);
            await WaitUntilAsync(() => !client2.IsConnected);

            Assert.True(client1.IsConnected);
            Assert.False(client2.IsConnected);

            await client1.DisconnectAsync();
            await server.StopAsync();
        }
    }
}
