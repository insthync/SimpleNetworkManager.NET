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

            Assert.True(server.ConnectionManager.TryGetConnection(1, out var clientConnection));
            Assert.NotNull(clientConnection);
            await clientConnection.DisconnectAsync();
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
            await client.ClientConnection.SendMessageAsync(new TestMessage()
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

            Assert.True(server.ConnectionManager.TryGetConnection(1, out var clientConnection));
            Assert.NotNull(clientConnection);
            await clientConnection.SendMessageAsync(new TestMessage()
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
    }
}
