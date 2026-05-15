using Insthync.SimpleNetworkManager.NET.Messages;
using Insthync.SimpleNetworkManager.NET.Messages.Error;
using Microsoft.Extensions.Logging;
using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Insthync.SimpleNetworkManager.NET.Network.WebSocketTransport
{
    public class WebSocketClientConnection : BaseClientConnection
    {
        private const int MinMessageSize = 8;
        private const int MaxMessageSize = 1024 * 1024;
        private const int ReceiveBufferSize = 8 * 1024;
        private const string WebSocketAcceptGuid = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";

        private readonly ClientWebSocket? _clientWebSocket;
        private readonly TcpClient? _tcpClient;
        private readonly NetworkStream? _networkStream;
        private readonly CancellationTokenSource _cancellationTokenSource;
        private readonly SemaphoreSlim _sendSemaphore;
        private bool _isConnected;

        public ClientWebSocket? ClientWebSocket => _clientWebSocket;
        public TcpClient? TcpClient => _tcpClient;
        public override bool IsConnected => _isConnected;

        public WebSocketClientConnection(ClientWebSocket clientWebSocket, ILogger<WebSocketClientConnection> logger) : base(logger)
        {
            _clientWebSocket = clientWebSocket ?? throw new ArgumentNullException(nameof(clientWebSocket));
            if (_clientWebSocket.State != WebSocketState.Open)
                throw new InvalidOperationException("ClientWebSocket is not connected.");

            _cancellationTokenSource = new CancellationTokenSource();
            _sendSemaphore = new SemaphoreSlim(1, 1);
            _isConnected = true;
        }

        private WebSocketClientConnection(TcpClient tcpClient, NetworkStream networkStream, ILogger<WebSocketClientConnection> logger) : base(logger)
        {
            _tcpClient = tcpClient ?? throw new ArgumentNullException(nameof(tcpClient));
            _networkStream = networkStream ?? throw new ArgumentNullException(nameof(networkStream));
            _cancellationTokenSource = new CancellationTokenSource();
            _sendSemaphore = new SemaphoreSlim(1, 1);
            _isConnected = true;
        }

        internal static async Task<WebSocketClientConnection> AcceptServerConnectionAsync(TcpClient tcpClient, ILogger<WebSocketClientConnection> logger, CancellationToken cancellationToken)
        {
            if (tcpClient == null)
                throw new ArgumentNullException(nameof(tcpClient));

            var stream = tcpClient.GetStream();
            string headerText = await ReadHttpHeaderAsync(stream, cancellationToken);
            var headers = ParseHeaders(headerText, out var requestLine);
            if (!requestLine.StartsWith("GET ", StringComparison.OrdinalIgnoreCase) ||
                !headers.TryGetValue("Upgrade", out var upgrade) ||
                !upgrade.Equals("websocket", StringComparison.OrdinalIgnoreCase) ||
                !headers.TryGetValue("Connection", out var connection) ||
                connection.IndexOf("Upgrade", StringComparison.OrdinalIgnoreCase) < 0 ||
                !headers.TryGetValue("Sec-WebSocket-Key", out var webSocketKey))
            {
                await WriteHttpResponseAsync(stream, "HTTP/1.1 400 Bad Request\r\nConnection: close\r\n\r\n", cancellationToken);
                throw new InvalidOperationException("Invalid WebSocket upgrade request.");
            }

            string acceptKey = CreateWebSocketAcceptKey(webSocketKey);
            string response =
                "HTTP/1.1 101 Switching Protocols\r\n" +
                "Upgrade: websocket\r\n" +
                "Connection: Upgrade\r\n" +
                $"Sec-WebSocket-Accept: {acceptKey}\r\n" +
                "\r\n";

            await WriteHttpResponseAsync(stream, response, cancellationToken);
            return new WebSocketClientConnection(tcpClient, stream, logger);
        }

        public async Task HandleConnectionAsync(CancellationToken cancellationToken)
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested && _isConnected)
                {
                    try
                    {
                        var message = await ReceiveMessageAsync(cancellationToken);
                        if (message == null)
                            break;

                        OnMessageReceived(message.Value.buffer, message.Value.length);
                        ArrayPool<byte>.Shared.Return(message.Value.buffer);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Error receiving WebSocket message for connection {ConnectionId}", ConnectionId);
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in WebSocket receive loop for connection {ConnectionId}", ConnectionId);
            }
            finally
            {
                if (_isConnected)
                {
                    _isConnected = false;
                    CleanupTransport();
                    FailPendingResponses(new IOException("WebSocket connection closed while waiting for a response."));
                    OnDisconnected();
                }
            }
        }

        private async Task<(byte[] buffer, int length)?> ReceiveMessageAsync(CancellationToken cancellationToken)
        {
            try
            {
                if (_clientWebSocket != null)
                    return await ReceiveClientWebSocketMessageAsync(cancellationToken);

                if (_networkStream != null)
                    return await ReceiveServerWebSocketMessageAsync(cancellationToken);

                return null;
            }
            catch (InvalidMessageSizeException sizeEx)
            {
                await SendErrorMessageAsync(MessageTypes.Error,
                    $"Invalid message size: {sizeEx.Size}. Must be between {sizeEx.MinSize} and {sizeEx.MaxSize} bytes.");
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error receiving WebSocket message for connection {ConnectionId}", ConnectionId);
                await SendErrorMessageAsync(MessageTypes.Error, "WebSocket message receive error");
                return null;
            }
        }

        private async Task<(byte[] buffer, int length)?> ReceiveClientWebSocketMessageAsync(CancellationToken cancellationToken)
        {
            byte[] receiveBuffer = ArrayPool<byte>.Shared.Rent(ReceiveBufferSize);
            try
            {
                using (var messageStream = new MemoryStream())
                {
                    WebSocketReceiveResult result;
                    do
                    {
                        result = await _clientWebSocket!.ReceiveAsync(new ArraySegment<byte>(receiveBuffer, 0, receiveBuffer.Length), cancellationToken);
                        if (result.MessageType == WebSocketMessageType.Close)
                            return null;
                        if (result.MessageType != WebSocketMessageType.Binary)
                        {
                            await SendErrorMessageAsync(MessageTypes.Error, "Only binary WebSocket messages are supported.");
                            return null;
                        }

                        messageStream.Write(receiveBuffer, 0, result.Count);
                        ValidateMessageSize(messageStream.Length);
                    }
                    while (!result.EndOfMessage);

                    return CopyToPooledBuffer(messageStream);
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(receiveBuffer);
            }
        }

        private async Task<(byte[] buffer, int length)?> ReceiveServerWebSocketMessageAsync(CancellationToken cancellationToken)
        {
            using (var messageStream = new MemoryStream())
            {
                bool receivingMessage = false;
                while (!cancellationToken.IsCancellationRequested)
                {
                    var frame = await ReadServerFrameAsync(cancellationToken);
                    if (frame == null)
                        return null;

                    try
                    {
                        switch (frame.Value.Opcode)
                        {
                            case 0x0:
                                if (!receivingMessage)
                                    throw new InvalidDataException("Unexpected WebSocket continuation frame.");
                                break;
                            case 0x2:
                                if (receivingMessage)
                                    throw new InvalidDataException("Unexpected nested WebSocket binary frame.");
                                receivingMessage = true;
                                break;
                            case 0x8:
                                await SendServerFrameAsync(0x8, Array.Empty<byte>(), 0, cancellationToken);
                                return null;
                            case 0x9:
                                await SendServerFrameAsync(0xA, frame.Value.Payload, frame.Value.PayloadLength, cancellationToken);
                                continue;
                            case 0xA:
                                continue;
                            default:
                                await SendErrorMessageAsync(MessageTypes.Error, "Only binary WebSocket messages are supported.");
                                return null;
                        }

                        messageStream.Write(frame.Value.Payload, 0, frame.Value.PayloadLength);
                        ValidateMessageSize(messageStream.Length);

                        if (frame.Value.Fin)
                            return CopyToPooledBuffer(messageStream);
                    }
                    finally
                    {
                        ArrayPool<byte>.Shared.Return(frame.Value.Payload);
                    }
                }

                return null;
            }
        }

        internal override async Task SendMessageAsync(BaseMessage message)
        {
            if (_disposed || !_isConnected)
            {
                _logger.LogWarning("Attempted to send WebSocket message to disconnected client {ConnectionId}", ConnectionId);
                return;
            }

            uint messageType = message.GetMessageType();
            bool sendSerializationError = false;
            bool sendNetworkError = false;
            bool shouldDisconnect = false;
            ExceptionDispatchInfo? capturedException = null;

            await _sendSemaphore.WaitAsync(_cancellationTokenSource.Token);
            try
            {
                try
                {
                    byte[] serializedData = message.Serialize();
                    if (_clientWebSocket != null)
                    {
                        await _clientWebSocket.SendAsync(new ArraySegment<byte>(serializedData, 0, serializedData.Length),
                            WebSocketMessageType.Binary, true, _cancellationTokenSource.Token);
                    }
                    else if (_networkStream != null)
                    {
                        await SendServerFrameAsync(0x2, serializedData, serializedData.Length, _cancellationTokenSource.Token);
                    }
                }
                catch (MessagePack.MessagePackSerializationException ex)
                {
                    _logger.LogError(ex, "MessagePack serialization failed for message type {MessageType} to WebSocket client {ConnectionId}",
                        messageType, ConnectionId);
                    sendSerializationError = true;
                }
            }
            catch (WebSocketException ex)
            {
                _logger.LogWarning(ex, "WebSocket error sending message type {MessageType} to connection {ConnectionId}", messageType, ConnectionId);
                sendNetworkError = true;
                shouldDisconnect = true;
                capturedException = ExceptionDispatchInfo.Capture(ex);
            }
            catch (IOException ex)
            {
                _logger.LogWarning(ex, "I/O error sending WebSocket message type {MessageType} to connection {ConnectionId}", messageType, ConnectionId);
                sendNetworkError = true;
                shouldDisconnect = true;
                capturedException = ExceptionDispatchInfo.Capture(ex);
            }
            catch (ObjectDisposedException ex)
            {
                _logger.LogDebug("WebSocket transport disposed while sending message to connection {ConnectionId}", ConnectionId);
                shouldDisconnect = true;
                capturedException = ExceptionDispatchInfo.Capture(ex);
            }
            catch (OperationCanceledException) when (_cancellationTokenSource.Token.IsCancellationRequested)
            {
                _logger.LogDebug("WebSocket send operation cancelled for connection {ConnectionId}", ConnectionId);
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error sending WebSocket message type {MessageType} to connection {ConnectionId}", messageType, ConnectionId);
                shouldDisconnect = true;
                capturedException = ExceptionDispatchInfo.Capture(ex);
            }
            finally
            {
                _sendSemaphore.Release();
            }

            if (sendSerializationError)
            {
                if (!MessageTypes.IsProtocolErrorMessageType(messageType))
                    await SendSerializationErrorAsync(messageType);
                return;
            }

            if (sendNetworkError)
            {
                if (!MessageTypes.IsProtocolErrorMessageType(messageType))
                    await SendErrorMessageAsync(MessageTypes.NetworkError, "WebSocket send error");
                if (shouldDisconnect)
                    await DisconnectAsync();
                capturedException?.Throw();
            }

            if (shouldDisconnect)
            {
                await DisconnectAsync();
                capturedException?.Throw();
            }
        }

        internal override async Task DisconnectAsync()
        {
            if (_disposed || !_isConnected)
                return;

            _isConnected = false;

            try
            {
                if (_clientWebSocket != null &&
                    (_clientWebSocket.State == WebSocketState.Open || _clientWebSocket.State == WebSocketState.CloseReceived))
                {
                    await _clientWebSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Disconnecting", CancellationToken.None);
                }
                else if (_networkStream != null)
                {
                    await SendServerFrameAsync(0x8, Array.Empty<byte>(), 0, CancellationToken.None);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error closing WebSocket connection {ConnectionId}", ConnectionId);
            }
            finally
            {
                _cancellationTokenSource.Cancel();
                CleanupTransport();
            }

            FailPendingResponses(new OperationCanceledException("WebSocket connection disconnected."));
            OnDisconnected();
        }

        public override async Task RejectConnectionAsync(ConnectionErrorTypes errorType, string errorText, bool shouldRetry, int retryDelayMs)
        {
            try
            {
                await SendConnectionErrorAsync(errorType, errorText, shouldRetry, retryDelayMs);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error sending WebSocket connection rejection message");
            }
            finally
            {
                await DisconnectAsync();
            }
        }

        private static async Task<string> ReadHttpHeaderAsync(NetworkStream stream, CancellationToken cancellationToken)
        {
            byte[] buffer = ArrayPool<byte>.Shared.Rent(256);
            try
            {
                using (var headerStream = new MemoryStream())
                {
                    while (headerStream.Length < 8192)
                    {
                        int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken);
                        if (bytesRead == 0)
                            throw new IOException("Connection closed during WebSocket handshake.");

                        headerStream.Write(buffer, 0, bytesRead);
                        if (ContainsHeaderTerminator(headerStream.GetBuffer(), (int)headerStream.Length))
                            return Encoding.ASCII.GetString(headerStream.GetBuffer(), 0, (int)headerStream.Length);
                    }
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }

            throw new InvalidDataException("WebSocket handshake header exceeded the maximum size.");
        }

        private static Dictionary<string, string> ParseHeaders(string headerText, out string requestLine)
        {
            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var lines = headerText.Split(new[] { "\r\n" }, StringSplitOptions.None);
            requestLine = lines.Length > 0 ? lines[0] : string.Empty;
            for (int i = 1; i < lines.Length; i++)
            {
                var line = lines[i];
                if (string.IsNullOrEmpty(line))
                    break;

                int separatorIndex = line.IndexOf(':');
                if (separatorIndex <= 0)
                    continue;

                headers[line.Substring(0, separatorIndex).Trim()] = line.Substring(separatorIndex + 1).Trim();
            }

            return headers;
        }

        private static string CreateWebSocketAcceptKey(string webSocketKey)
        {
            using (var sha1 = SHA1.Create())
            {
                byte[] hash = sha1.ComputeHash(Encoding.ASCII.GetBytes(webSocketKey + WebSocketAcceptGuid));
                return Convert.ToBase64String(hash);
            }
        }

        private static Task WriteHttpResponseAsync(NetworkStream stream, string response, CancellationToken cancellationToken)
        {
            byte[] responseBytes = Encoding.ASCII.GetBytes(response);
            return stream.WriteAsync(responseBytes, 0, responseBytes.Length, cancellationToken);
        }

        private static bool ContainsHeaderTerminator(byte[] buffer, int length)
        {
            for (int i = 3; i < length; i++)
            {
                if (buffer[i - 3] == '\r' && buffer[i - 2] == '\n' && buffer[i - 1] == '\r' && buffer[i] == '\n')
                    return true;
            }

            return false;
        }

        private async Task<WebSocketFrame?> ReadServerFrameAsync(CancellationToken cancellationToken)
        {
            if (_networkStream == null)
                return null;

            byte[] header = ArrayPool<byte>.Shared.Rent(2);
            try
            {
                int bytesRead = await _networkStream.ReadExactAsync(header.AsMemory(0, 2), 2, cancellationToken);
                if (bytesRead < 2)
                    return null;

                bool fin = (header[0] & 0x80) != 0;
                byte opcode = (byte)(header[0] & 0x0F);
                bool masked = (header[1] & 0x80) != 0;
                ulong payloadLength = (ulong)(header[1] & 0x7F);

                if (!masked)
                    throw new InvalidDataException("Client WebSocket frames must be masked.");

                if (payloadLength == 126)
                {
                    byte[] extendedLength = ArrayPool<byte>.Shared.Rent(2);
                    try
                    {
                        bytesRead = await _networkStream.ReadExactAsync(extendedLength.AsMemory(0, 2), 2, cancellationToken);
                        if (bytesRead < 2)
                            return null;
                        payloadLength = BinaryPrimitives.ReadUInt16BigEndian(extendedLength.AsSpan(0, 2));
                    }
                    finally
                    {
                        ArrayPool<byte>.Shared.Return(extendedLength);
                    }
                }
                else if (payloadLength == 127)
                {
                    byte[] extendedLength = ArrayPool<byte>.Shared.Rent(8);
                    try
                    {
                        bytesRead = await _networkStream.ReadExactAsync(extendedLength.AsMemory(0, 8), 8, cancellationToken);
                        if (bytesRead < 8)
                            return null;
                        payloadLength = BinaryPrimitives.ReadUInt64BigEndian(extendedLength.AsSpan(0, 8));
                    }
                    finally
                    {
                        ArrayPool<byte>.Shared.Return(extendedLength);
                    }
                }

                if (payloadLength > MaxMessageSize)
                    throw new InvalidMessageSizeException() { Size = payloadLength > int.MaxValue ? int.MaxValue : (int)payloadLength, MinSize = MinMessageSize, MaxSize = MaxMessageSize };

                byte[] mask = ArrayPool<byte>.Shared.Rent(4);
                try
                {
                    bytesRead = await _networkStream.ReadExactAsync(mask.AsMemory(0, 4), 4, cancellationToken);
                    if (bytesRead < 4)
                        return null;

                    int payloadLengthInt = (int)payloadLength;
                    byte[] payload = ArrayPool<byte>.Shared.Rent(payloadLengthInt);
                    bytesRead = await _networkStream.ReadExactAsync(payload.AsMemory(0, payloadLengthInt), payloadLengthInt, cancellationToken);
                    if (bytesRead < payloadLengthInt)
                    {
                        ArrayPool<byte>.Shared.Return(payload);
                        return null;
                    }

                    for (int i = 0; i < payloadLengthInt; i++)
                        payload[i] = (byte)(payload[i] ^ mask[i % 4]);

                    return new WebSocketFrame(fin, opcode, payload, payloadLengthInt);
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(mask);
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(header);
            }
        }

        private async Task SendServerFrameAsync(byte opcode, byte[] payload, int length, CancellationToken cancellationToken)
        {
            if (_networkStream == null)
                return;

            byte[] header = ArrayPool<byte>.Shared.Rent(10);
            try
            {
                int headerLength = 0;
                header[headerLength++] = (byte)(0x80 | opcode);
                if (length <= 125)
                {
                    header[headerLength++] = (byte)length;
                }
                else if (length <= ushort.MaxValue)
                {
                    header[headerLength++] = 126;
                    BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(headerLength, 2), (ushort)length);
                    headerLength += 2;
                }
                else
                {
                    header[headerLength++] = 127;
                    BinaryPrimitives.WriteUInt64BigEndian(header.AsSpan(headerLength, 8), (ulong)length);
                    headerLength += 8;
                }

                await _networkStream.WriteAsync(header, 0, headerLength, cancellationToken);
                if (length > 0)
                    await _networkStream.WriteAsync(payload, 0, length, cancellationToken);
                await _networkStream.FlushAsync(cancellationToken);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(header);
            }
        }

        private static void ValidateMessageSize(long length)
        {
            if (length < MinMessageSize)
                return;
            if (length > MaxMessageSize)
                throw new InvalidMessageSizeException() { Size = length > int.MaxValue ? int.MaxValue : (int)length, MinSize = MinMessageSize, MaxSize = MaxMessageSize };
        }

        private static (byte[] buffer, int length) CopyToPooledBuffer(MemoryStream messageStream)
        {
            int length = (int)messageStream.Length;
            if (length < MinMessageSize || length > MaxMessageSize)
                throw new InvalidMessageSizeException() { Size = length, MinSize = MinMessageSize, MaxSize = MaxMessageSize };

            byte[] buffer = ArrayPool<byte>.Shared.Rent(length);
            Array.Copy(messageStream.GetBuffer(), 0, buffer, 0, length);
            return (buffer, length);
        }

        private void CleanupTransport()
        {
            try
            {
                _clientWebSocket?.Dispose();
                _networkStream?.Dispose();
                _tcpClient?.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error cleaning up WebSocket transport {ConnectionId}", ConnectionId);
            }
        }

        public override void Dispose()
        {
            if (_disposed)
                return;

            base.Dispose();

            _cancellationTokenSource.Cancel();
            _sendSemaphore.Dispose();
            CleanupTransport();
        }

        private struct WebSocketFrame
        {
            public readonly bool Fin;
            public readonly byte Opcode;
            public readonly byte[] Payload;
            public readonly int PayloadLength;

            public WebSocketFrame(bool fin, byte opcode, byte[] payload, int payloadLength)
            {
                Fin = fin;
                Opcode = opcode;
                Payload = payload;
                PayloadLength = payloadLength;
            }
        }
    }
}
