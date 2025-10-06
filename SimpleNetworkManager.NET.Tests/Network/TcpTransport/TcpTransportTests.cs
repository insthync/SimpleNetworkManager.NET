using Insthync.SimpleNetworkManager.NET.Network.TcpTransport;
using Insthync.SimpleNetworkManager.NET.Tests.Messages;
using Microsoft.Extensions.Logging;
using Moq;

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

        [Fact]
        public async Task TestSimpleConnection()
        {
            var server = new TcpNetworkServer(_loggerFactoryMock.Object);
            var client = new TcpNetworkClient(_loggerFactoryMock.Object);

            var serverCancelSrc = new CancellationTokenSource();
            await server.StartAsync(7890, serverCancelSrc.Token);

            var clientCancelSrc = new CancellationTokenSource();
            await client.ConnectAsync("127.0.0.1", 7890, clientCancelSrc.Token);

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
            var server = new TcpNetworkServer(_loggerFactoryMock.Object);
            var client = new TcpNetworkClient(_loggerFactoryMock.Object);

            var serverCancelSrc = new CancellationTokenSource();
            await server.StartAsync(7891, serverCancelSrc.Token);

            var clientCancelSrc = new CancellationTokenSource();
            await client.ConnectAsync("127.0.0.1", 7891, clientCancelSrc.Token);

            Assert.True(server.IsRunning);
            Assert.True(client.IsConnected);
            // Wait a second for connection acceptance
            await Task.Delay(1000);
            Assert.Equal(1, server.ConnectionManager.ConnectionCount);

            await server.DisconnectAsync(1);
            Assert.Equal(0, server.ConnectionManager.ConnectionCount);

            // Wait a second for disconnection
            await Task.Delay(1000);
            Assert.False(client.IsConnected);

            await server.StopAsync();

            Assert.False(server.IsRunning);
        }

        [Fact]
        public async Task TestClientDisconnectionFromClient()
        {
            var server = new TcpNetworkServer(_loggerFactoryMock.Object);
            var client = new TcpNetworkClient(_loggerFactoryMock.Object);

            var serverCancelSrc = new CancellationTokenSource();
            await server.StartAsync(7892, serverCancelSrc.Token);

            var clientCancelSrc = new CancellationTokenSource();
            await client.ConnectAsync("127.0.0.1", 7892, clientCancelSrc.Token);

            Assert.True(server.IsRunning);
            Assert.True(client.IsConnected);
            // Wait a second for connection acceptance
            await Task.Delay(1000);
            Assert.Equal(1, server.ConnectionManager.ConnectionCount);

            await client.DisconnectAsync();
            // Wait a second for disconnection
            await Task.Delay(1000);
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

            var serverCancelSrc = new CancellationTokenSource();
            await server.StartAsync(7893, serverCancelSrc.Token);

            var clientCancelSrc = new CancellationTokenSource();
            await client.ConnectAsync("127.0.0.1", 7893, clientCancelSrc.Token);

            Assert.True(server.IsRunning);
            Assert.True(client.IsConnected);

            Assert.NotNull(client.ClientConnection);
            await client.SendMessageAsync(new TestMessage()
            {
                stringVal = "HelloMsgClient",
            });

            // Wait a second for message sending
            await Task.Delay(1000);
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

            var serverCancelSrc = new CancellationTokenSource();
            await server.StartAsync(7894, serverCancelSrc.Token);

            var clientCancelSrc = new CancellationTokenSource();
            await client.ConnectAsync("127.0.0.1", 7894, clientCancelSrc.Token);

            Assert.True(server.IsRunning);
            Assert.True(client.IsConnected);

            // Wait a second for connection acceptance
            await Task.Delay(1000);
            Assert.Equal(1, server.ConnectionManager.ConnectionCount);

            await server.SendMessageAsync(1, new TestMessage()
            {
                stringVal = "HelloMsgFromServer",
            });

            // Wait a second for message sending
            await Task.Delay(1000);
            Assert.Equal("HelloMsgFromServer", clientTestMsgHandler.stringVal);

            await client.DisconnectAsync();
            await server.StopAsync();

            Assert.False(server.IsRunning);
            Assert.False(client.IsConnected);
        }

        [Fact]
        public async Task TestReuseClientConnection()
        {
            var server = new TcpNetworkServer(_loggerFactoryMock.Object);
            var client = new TcpNetworkClient(_loggerFactoryMock.Object);

            var serverCancelSrc = new CancellationTokenSource();
            await server.StartAsync(7895, serverCancelSrc.Token);
            Assert.True(server.IsRunning);

            for (int i = 0; i < 10; ++i)
            {
                var clientCancelSrc = new CancellationTokenSource();
                await client.ConnectAsync("127.0.0.1", 7895, clientCancelSrc.Token);
                // Wait a bit for connection acceptance
                await Task.Delay(100);
                Assert.True(client.IsConnected);
                await client.DisconnectAsync();
                // Wait a bit for disconnection
                await Task.Delay(100);
                Assert.False(client.IsConnected);
            }

            await server.StopAsync();

            Assert.False(server.IsRunning);
        }

        [Fact]
        public async Task TestClientMaxConnections()
        {
            var server = new TcpNetworkServer(_loggerFactoryMock.Object);
            server.MaxConnections = 2;

            var serverCancelSrc = new CancellationTokenSource();
            await server.StartAsync(7896, serverCancelSrc.Token);
            Assert.True(server.IsRunning);

            // Client 1 - must be able to connection
            var client1 = new TcpNetworkClient(_loggerFactoryMock.Object);
            var clientCancelSrc = new CancellationTokenSource();
            await client1.ConnectAsync("127.0.0.1", 7896, clientCancelSrc.Token);
            // Wait a second for connection acceptance
            await Task.Delay(1000);
            Assert.True(client1.IsConnected);

            // Client 2 - must be able to connection
            var client2 = new TcpNetworkClient(_loggerFactoryMock.Object);
            clientCancelSrc = new CancellationTokenSource();
            await client2.ConnectAsync("127.0.0.1", 7896, clientCancelSrc.Token);
            // Wait a second for connection acceptance
            await Task.Delay(1000);
            Assert.True(client2.IsConnected);

            // Client 3 - must not be able to connection
            var client3 = new TcpNetworkClient(_loggerFactoryMock.Object);
            clientCancelSrc = new CancellationTokenSource();
            await client3.ConnectAsync("127.0.0.1", 7896, clientCancelSrc.Token);
            // Wait a second for connection acceptance
            await Task.Delay(1000);
            Assert.False(client3.IsConnected);

            await client1.DisconnectAsync();
            await client2.DisconnectAsync();
            try
            {
                await client3.DisconnectAsync();
            } catch (Exception ex)
            {
                Assert.IsType<InvalidOperationException>(ex);
            }

            await server.StopAsync();

            Assert.False(server.IsRunning);
        }
    }
}
