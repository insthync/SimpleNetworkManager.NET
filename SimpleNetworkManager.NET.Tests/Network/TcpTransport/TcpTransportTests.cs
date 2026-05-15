using Insthync.SimpleNetworkManager.NET.Network.TcpTransport;
using Insthync.SimpleNetworkManager.NET.Tests.Messages;
using Microsoft.Extensions.Logging;
using Moq;
using System.Net;
using System.Net.Sockets;

namespace Insthync.SimpleNetworkManager.NET.Tests.Network.TcpTransport
{
    public class TcpTransportTests
    {
        private readonly Mock<ILoggerFactory> _loggerFactoryMock;
        private readonly Mock<ILogger> _loggerMock;

        public TcpTransportTests()
        {
            _loggerMock = new Mock<ILogger>();
            _loggerFactoryMock = new Mock<ILoggerFactory>();

            _loggerFactoryMock
                .Setup(f => f.CreateLogger(It.IsAny<string>()))
                .Returns(_loggerMock.Object);
        }

        private async Task<TcpNetworkServer> StartServerAsync(int maxConnections = 1)
        {
            var server = new TcpNetworkServer(_loggerFactoryMock.Object)
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

        private static int GetUnusedTcpPort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        [Fact]
        public async Task TestSimpleConnection()
        {
            var server = await StartServerAsync();
            var client = new TcpNetworkClient(_loggerFactoryMock.Object);

            await client.ConnectAsync("127.0.0.1", server.RunningPort, CancellationToken.None);

            Assert.True(server.IsRunning);
            Assert.True(client.IsConnected);

            await client.DisconnectAsync();
            await server.StopAsync();

            Assert.False(server.IsRunning);
            Assert.False(client.IsConnected);
        }

        [Fact]
        public async Task TestClientDisconnectionFromServer()
        {
            var server = await StartServerAsync();
            var client = new TcpNetworkClient(_loggerFactoryMock.Object);

            await client.ConnectAsync("127.0.0.1", server.RunningPort, CancellationToken.None);

            Assert.True(server.IsRunning);
            Assert.True(client.IsConnected);
            await WaitUntilAsync(() => server.ConnectionManager.ConnectionCount == 1);
            Assert.Equal(1, server.ConnectionManager.ConnectionCount);

            await server.DisconnectAsync(server.ConnectionManager.GetAllConnections().First().ConnectionId);
            await WaitUntilAsync(() => server.ConnectionManager.ConnectionCount == 0);
            Assert.Equal(0, server.ConnectionManager.ConnectionCount);

            await WaitUntilAsync(() => !client.IsConnected);
            Assert.False(client.IsConnected);

            await server.StopAsync();

            Assert.False(server.IsRunning);
        }

        [Fact]
        public async Task TestClientDisconnectionFromClient()
        {
            var server = await StartServerAsync();
            var client = new TcpNetworkClient(_loggerFactoryMock.Object);

            await client.ConnectAsync("127.0.0.1", server.RunningPort, CancellationToken.None);

            Assert.True(server.IsRunning);
            Assert.True(client.IsConnected);
            await WaitUntilAsync(() => server.ConnectionManager.ConnectionCount == 1);
            Assert.Equal(1, server.ConnectionManager.ConnectionCount);

            await client.DisconnectAsync();
            await WaitUntilAsync(() => server.ConnectionManager.ConnectionCount == 0);
            Assert.Equal(0, server.ConnectionManager.ConnectionCount);

            Assert.False(client.IsConnected);

            await server.StopAsync();

            Assert.False(server.IsRunning);
        }

        [Fact]
        public async Task TestMessageFromClientHandling()
        {
            var server = new TcpNetworkServer(_loggerFactoryMock.Object);
            var serverTestMsgHandler = new TestMessageHandler();
            server.MessageRouterService.RegisterHandler(serverTestMsgHandler);
            var client = new TcpNetworkClient(_loggerFactoryMock.Object);
            var clientTestMsgHandler = new TestMessageHandler();
            client.MessageRouterService.RegisterHandler(clientTestMsgHandler);

            await server.StartAsync(0, CancellationToken.None);

            await client.ConnectAsync("127.0.0.1", server.RunningPort, CancellationToken.None);

            Assert.True(server.IsRunning);
            Assert.True(client.IsConnected);

            Assert.NotNull(client.ClientConnection);
            await client.SendMessageAsync(new TestMessage()
            {
                stringVal = "HelloMsgClient",
            });

            await WaitUntilAsync(() => serverTestMsgHandler.stringVal == "HelloMsgClient");
            Assert.Equal("HelloMsgClient", serverTestMsgHandler.stringVal);

            await client.DisconnectAsync();
            await server.StopAsync();

            Assert.False(server.IsRunning);
            Assert.False(client.IsConnected);
        }

        [Fact]
        public async Task TestMessageFromServerHandling()
        {
            var server = new TcpNetworkServer(_loggerFactoryMock.Object);
            var serverTestMsgHandler = new TestMessageHandler();
            server.MessageRouterService.RegisterHandler(serverTestMsgHandler);
            var client = new TcpNetworkClient(_loggerFactoryMock.Object);
            var clientTestMsgHandler = new TestMessageHandler();
            client.MessageRouterService.RegisterHandler(clientTestMsgHandler);

            await server.StartAsync(0, CancellationToken.None);

            await client.ConnectAsync("127.0.0.1", server.RunningPort, CancellationToken.None);

            Assert.True(server.IsRunning);
            Assert.True(client.IsConnected);

            await WaitUntilAsync(() => server.ConnectionManager.ConnectionCount == 1);
            Assert.Equal(1, server.ConnectionManager.ConnectionCount);

            await server.SendMessageAsync(server.ConnectionManager.GetAllConnections().First().ConnectionId, new TestMessage()
            {
                stringVal = "HelloMsgFromServer",
            });

            await WaitUntilAsync(() => clientTestMsgHandler.stringVal == "HelloMsgFromServer");
            Assert.Equal("HelloMsgFromServer", clientTestMsgHandler.stringVal);

            await client.DisconnectAsync();
            await server.StopAsync();

            Assert.False(server.IsRunning);
            Assert.False(client.IsConnected);
        }

        [Fact]
        public async Task TestReuseClientConnection()
        {
            var server = await StartServerAsync();
            var client = new TcpNetworkClient(_loggerFactoryMock.Object);

            Assert.True(server.IsRunning);

            for (int i = 0; i < 10; ++i)
            {
                await client.ConnectAsync("127.0.0.1", server.RunningPort, CancellationToken.None);
                Assert.True(client.IsConnected);
                await client.DisconnectAsync();
                await WaitUntilAsync(() => !client.IsConnected);
                await WaitUntilAsync(() => server.ConnectionManager.ConnectionCount == 0);
                Assert.False(client.IsConnected);
            }

            await server.StopAsync();

            Assert.False(server.IsRunning);
        }

        [Fact]
        public async Task TestClientMaxConnections()
        {
            var server = await StartServerAsync(maxConnections: 2);
            Assert.True(server.IsRunning);

            // Client 1 - must be able to connection
            var client1 = new TcpNetworkClient(_loggerFactoryMock.Object);
            await client1.ConnectAsync("127.0.0.1", server.RunningPort, CancellationToken.None);
            Assert.True(client1.IsConnected);

            // Client 2 - must be able to connection
            var client2 = new TcpNetworkClient(_loggerFactoryMock.Object);
            await client2.ConnectAsync("127.0.0.1", server.RunningPort, CancellationToken.None);
            Assert.True(client2.IsConnected);
            await WaitUntilAsync(() => server.ConnectionManager.ConnectionCount == 2);

            // Client 3 - must not be able to connection
            var client3 = new TcpNetworkClient(_loggerFactoryMock.Object);
            await client3.ConnectAsync("127.0.0.1", server.RunningPort, CancellationToken.None);
            await WaitUntilAsync(() => !client3.IsConnected);
            Assert.False(client3.IsConnected);

            await client1.DisconnectAsync();
            await client2.DisconnectAsync();
            await Assert.ThrowsAsync<InvalidOperationException>(() => client3.DisconnectAsync());

            await server.StopAsync();

            Assert.False(server.IsRunning);
        }

        [Fact]
        public async Task TestRequestResponse()
        {
            var server = new TcpNetworkServer(_loggerFactoryMock.Object);
            var serverTestMsgHandler = new TestRequestMessageHandler();
            server.MessageRouterService.RegisterHandler(serverTestMsgHandler);
            await server.StartAsync(0, CancellationToken.None);

            var client = new TcpNetworkClient(_loggerFactoryMock.Object);
            await client.ConnectAsync("127.0.0.1", server.RunningPort, CancellationToken.None);

            Assert.True(server.IsRunning);
            Assert.True(client.IsConnected);

            Assert.NotNull(client.ClientConnection);
            var response = await client.SendRequestAsync<TestResponseMessage>(new TestRequestMessage()
            {
                stringVal = "Hello",
            });

            Assert.Equal("Hello_Hello", response.stringVal);

            await client.DisconnectAsync();
            await server.StopAsync();

            Assert.False(server.IsRunning);
            Assert.False(client.IsConnected);
        }

        [Fact]
        public async Task ConnectAsync_WhenServerUnavailable_ThrowsAndLeavesClientDisconnected()
        {
            var client = new TcpNetworkClient(_loggerFactoryMock.Object);
            int unusedPort = GetUnusedTcpPort();

            await Assert.ThrowsAsync<SocketException>(() => client.ConnectAsync("127.0.0.1", unusedPort, CancellationToken.None));

            Assert.False(client.IsConnected);
            Assert.Null(client.ClientConnection);
        }
    }
}
