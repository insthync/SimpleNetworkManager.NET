# SimpleNetworkManager.NET
A simple .NET (TCP + MessagePack) network manager.

Wire frames use a fixed little-endian header: `size:int32` + `messageType:uint32` + MessagePack payload.

## How to serve and make connection
TCP transport:
```
// Run a server at port 7890
var server = new TcpNetworkServer(_loggerFactoryMock.Object);
await server.StartAsync(7890, CancellationToken.None);
```

Or bind to any available port and read the assigned port:
```
var server = new TcpNetworkServer(_loggerFactoryMock.Object);
await server.StartAsync(0, CancellationToken.None);
var port = server.RunningPort;
```

```
// Connect to server at 127.0.0.1:7890
var client = new TcpNetworkClient(_loggerFactoryMock.Object);
await client.ConnectAsync("127.0.0.1", 7890, CancellationToken.None);
```

`ConnectAsync` throws if the TCP connection cannot be established, and the client remains disconnected after a failed attempt.

WebSocket transport:
```
using Insthync.SimpleNetworkManager.NET.Network.WebSocketTransport;

// Run a WebSocket server at port 7890
var server = new WebSocketNetworkServer(_loggerFactoryMock.Object);
await server.StartAsync(7890, CancellationToken.None);

// Connect to ws://127.0.0.1:7890/
var client = new WebSocketNetworkClient(_loggerFactoryMock.Object);
await client.ConnectAsync("127.0.0.1", 7890, CancellationToken.None);
```

To connect to a custom WebSocket path, set `Path` before connecting or use the URI overload.

```
var client = new WebSocketNetworkClient(_loggerFactoryMock.Object)
{
    Path = "/network"
};
await client.ConnectAsync("127.0.0.1", 7890, CancellationToken.None);

await client.ConnectAsync(new Uri("ws://127.0.0.1:7890/network"), CancellationToken.None);
```

The WebSocket transport uses binary WebSocket messages. The payload inside each WebSocket message is the same SimpleNetworkManager.NET frame format.

## How to stop, disconnect
```
await client.DisconnectAsync();
await server.StopAsync();
```

## How to disconnect by server
```
uint connectionId = 1;
await server.DisconnectAsync(connectionId);
```

## How to create network message class
```
using Insthync.SimpleNetworkManager.NET.Messages;
using MessagePack;

[MessagePackObject] // Required by MessagePack-CSharp
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
When a server or client receives a message registered with its router, the matching handler processes it.
```
using Insthync.SimpleNetworkManager.NET.Messages;
using Insthync.SimpleNetworkManager.NET.Network;

public class TestMessageHandler : BaseMessageHandler<TestMessage>
{
    public int intVal;
    public bool boolVal;
    public string? stringVal;

    protected override Task HandleAsync(BaseClientConnection clientConnection, TestMessage data)
    {
        intVal = data.intVal;
        boolVal = data.boolVal;
        stringVal = data.stringVal;
        return Task.CompletedTask;
    }
}
```

## How to register handler to message router
```
server.RegisterHandler(serverTestMsgHandler);
client.RegisterHandler(clientTestMsgHandler);
```

You can also access the router directly:
```
server.MessageRouterService.RegisterHandler(serverTestMsgHandler);
client.MessageRouterService.RegisterHandler(clientTestMsgHandler);
```

## How to use request and response
Request/response messages are useful when one side needs to send a message and wait for a typed reply. The sender calls `SendRequestAsync<TResponse>()`, and the receiver registers a `BaseRequestMessageHandler<TRequest, TResponse>`.

`BaseRequestMessage` reserves MessagePack key `0` for `RequestId`, so request message fields should start at key `1`.

```
using Insthync.SimpleNetworkManager.NET.Messages;
using MessagePack;

[MessagePackObject]
public class LoginRequestMessage : BaseRequestMessage
{
    public override uint GetMessageType()
    {
        return 10;
    }

    [Key(1)]
    public string? Username;

    [Key(2)]
    public string? Password;
}
```

`BaseResponseMessage` reserves keys `0`, `1`, and `2` for `RequestId`, `Success`, and `ErrorMessage`, so response message fields should start at key `3`.

```
using Insthync.SimpleNetworkManager.NET.Messages;
using MessagePack;

[MessagePackObject]
public class LoginResponseMessage : BaseResponseMessage
{
    public override uint GetMessageType()
    {
        return 11;
    }

    [Key(3)]
    public string? SessionToken;
}
```

Create a request handler on the side that receives the request. The base handler copies the request ID into the response and sends it back automatically.

```
using Insthync.SimpleNetworkManager.NET.Messages;
using Insthync.SimpleNetworkManager.NET.Network;

public class LoginRequestMessageHandler : BaseRequestMessageHandler<LoginRequestMessage, LoginResponseMessage>
{
    protected override Task<LoginResponseMessage> HandleRequestAsync(BaseClientConnection clientConnection, LoginRequestMessage request)
    {
        if (request.Username != "demo" || request.Password != "password")
        {
            return Task.FromResult(new LoginResponseMessage()
            {
                Success = false,
                ErrorMessage = "Invalid username or password",
            });
        }

        return Task.FromResult(new LoginResponseMessage()
        {
            Success = true,
            SessionToken = "session-token",
        });
    }
}
```

Register the request handler on the receiver.

```
server.RegisterHandler(new LoginRequestMessageHandler());
```

Send the request from the other side and wait for the typed response. The default timeout is 10 seconds, or you can pass `timeoutMs`.

```
var response = await client.SendRequestAsync<LoginResponseMessage>(new LoginRequestMessage()
{
    Username = "demo",
    Password = "password",
}, timeoutMs: 5_000);

if (response.Success)
{
    var sessionToken = response.SessionToken;
}
else
{
    var error = response.ErrorMessage;
}
```

Servers can also send requests to a specific client connection. In that direction, register the request handler on the client because the client receives the request.

```
client.RegisterHandler(new LoginRequestMessageHandler());

uint connectionId = server.ConnectionManager.GetAllConnections().First().ConnectionId;
var response = await server.SendRequestAsync<LoginResponseMessage>(connectionId, new LoginRequestMessage()
{
    Username = "demo",
    Password = "password",
});
```

If no matching response arrives before the timeout, `SendRequestAsync` throws `TimeoutException`.
