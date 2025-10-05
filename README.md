# simple-network-manager
A simple .NET (TCP+MessagePack) network manager

## How to serve and make connection
```
// Run a server at port 7890
var server = new TcpNetworkServer(_loggerFactoryMock.Object);
var serverCancelSrc = new CancellationTokenSource();
await server.StartAsync(7890, serverCancelSrc.Token);

// Connect to server at 127.0.0.1:7890
var client = new TcpNetworkClient(_loggerFactoryMock.Object);
var clientCancelSrc = new CancellationTokenSource();
await client.ConnectAsync("127.0.0.1", 7890, clientCancelSrc.Token);
```

## How to stop, disconnect
```
await client.DisconnectAsync();
await server.StopAsync();
```

## How to disconnect by server
```
uint connectionId = 1;
if (server.ConnectionManager.TryGetConnection(connectionId, out var clientConnection))
    await clientConnection.DisconnectAsync();
```

## How to create network message class
```
using Insthync.SimpleNetworkManager.NET.Messages;
using MessagePack;

[MessagePackObject] // <- This must be message pack object, info: https://github.com/MessagePack-CSharp/MessagePack-CSharp
public class TestMessage : BaseMessage
{
    public override uint GetMessageType()
    {
        // This must be unique
        return 1;
    }

    [Key(0)]
    public int intVal;

    [Key(1)]
    public bool boolVal;

    [Key(2)]
    public string? stringVal;
}
```

## How to create network message handler class
When server or client receive message which already registered to message router, it will do whats it was desinated in the handler
```
using Cysharp.Threading.Tasks;
using Insthync.SimpleNetworkManager.NET.Messages;

public class TestMessageHandler : BaseMessageHandler<TestMessage>
{
    public int intVal;
    public bool boolVal;
    public string? stringVal;

    protected override UniTask HandleAsync(TestMessage data)
    {
        intVal = data.intVal;
        boolVal = data.boolVal;
        stringVal = data.stringVal;
        return default;
    }
}
```

## How to register handler to message router
```
server.MessageRouter.RegisterHandler(serverTestMsgHandler);
client.MessageRouter.RegisterHandler(clientTestMsgHandler);
```
