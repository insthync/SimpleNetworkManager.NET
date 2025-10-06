using Cysharp.Threading.Tasks;
using Insthync.SimpleNetworkManager.NET.Messages;
using Insthync.SimpleNetworkManager.NET.Messages.Error;
using Microsoft.Extensions.Logging;
using System;
using System.Buffers;
using System.IO;
using System.Net.Sockets;
using System.Threading;

namespace Insthync.SimpleNetworkManager.NET.Network.TcpTransport
{
    public class TcpClientConnection : BaseClientConnection
    {
        private readonly TcpClient _tcpClient;
        private readonly NetworkStream? _networkStream;
        private readonly CancellationTokenSource _cancellationTokenSource;
        private readonly SemaphoreSlim _sendSemaphore;

        private UniTask? _receiveTask;
        private bool _isConnected;

        public TcpClient TcpClient => _tcpClient;
        public override bool IsConnected => _isConnected;

        /// <summary>
        /// Initializes a new TcpClientConnection instance
        /// </summary>
        /// <param name="tcpClient">The underlying TCP client</param>
        /// <param name="logger">Logger instance</param>
        /// <param name="connectionTimeoutMs">Connection timeout in milliseconds (default: 30000)</param>
        public TcpClientConnection(TcpClient tcpClient, ILogger<TcpClientConnection> logger, int connectionTimeoutMs = 30000) : base(logger)
        {
            _tcpClient = tcpClient ?? throw new ArgumentNullException(nameof(tcpClient));

            _cancellationTokenSource = new CancellationTokenSource();
            _sendSemaphore = new SemaphoreSlim(1, 1);
            _cancellationTokenSource = new CancellationTokenSource();
            _sendSemaphore = new SemaphoreSlim(1, 1);

            // Configure connection timeouts with error handling
            try
            {
                _tcpClient.ReceiveTimeout = connectionTimeoutMs;
                _tcpClient.SendTimeout = connectionTimeoutMs;
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogError(ex, "Cannot set timeout on already connected client {ConnectionId}", ConnectionId);
                throw new Exception("Failed to configure connection timeouts", ex);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to set timeout for client {ConnectionId}", ConnectionId);
            }

            // Only get stream and start receive loop if client is connected
            if (_tcpClient.Connected)
            {
                try
                {
                    _networkStream = _tcpClient.GetStream();
                    _isConnected = true;
                    _logger.LogInformation("Client connection established with ConnectionId: {ConnectionId}", ConnectionId);
                }
                catch (InvalidOperationException ex)
                {
                    _logger.LogError(ex, "Failed to get network stream - client not connected {ConnectionId}", ConnectionId);
                    _isConnected = false;
                    throw new Exception("Client connection is not in a valid state", ex);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to initialize network stream for client {ConnectionId}", ConnectionId);
                    _isConnected = false;
                    throw new Exception("Failed to initialize client connection", ex);
                }
            }
            else
            {
                _isConnected = false;
                _logger.LogWarning("ClientConnection created with non-connected TcpClient for ConnectionId: {ConnectionId}", ConnectionId);
                throw new Exception("TcpClient is not connected");
            }
        }

        public async UniTask HandleConnectionAsync(CancellationToken cancellationToken)
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested && _isConnected)
                {
                    try
                    {
                        var message = await ReceiveMessageAsync(cancellationToken);
                        if (message == null)
                        {
                            _logger.LogDebug("Client {ConnectionId} disconnected during message receive", ConnectionId);
                            break; // Connection closed
                        }
                        OnMessageReceived(message.Value.buffer, message.Value.length);
                        ArrayPool<byte>.Shared.Return(message.Value.buffer);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        _logger.LogDebug("Receive loop cancelled for client {ConnectionId}", ConnectionId);
                        break;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Error receiving message from client {ConnectionId}", ConnectionId);
                        break; // Exit loop on any receive error
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in receive loop for client {ConnectionId}", ConnectionId);
            }
            finally
            {
                if (_isConnected)
                {
                    // Don't call DisconnectAsync from within the receive loop to avoid deadlock
                    // Just mark as disconnected and raise the event
                    _isConnected = false;
                    try
                    {
                        _networkStream?.Close();
                        _tcpClient?.Close();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Error during client {ConnectionId} cleanup", ConnectionId);
                    }
                    OnDisconnected();
                }
            }
        }

        /// <summary>
        /// Receives a complete message from the network stream with timeout handling
        /// message buffer should be returned to array pool after use
        /// </summary>
        private async UniTask<(byte[] buffer, int length)?> ReceiveMessageAsync(CancellationToken cancellationToken)
        {
            if (_networkStream == null || !_isConnected)
                return null;

            try
            {
                // Add timeout for message receiving (30 seconds)
                using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken, timeoutCts.Token);

                try
                {
                    return await _networkStream.ReadMessageAsync(combinedCts.Token);
                }
                catch (InvalidMessageSizeException sizeEx)
                {
                    _logger.LogWarning("Invalid message size {MessageSize} from client {ConnectionId}",
                        sizeEx.Size, ConnectionId);

                    // Send error about invalid message size
                    await SendErrorMessageAsync(MessageTypes.Error,
                        $"Invalid message size: {sizeEx.Size}. Must be between {sizeEx.MinSize} and {sizeEx.MaxSize} bytes.");
                    return null;
                }
            }
            catch (OperationCanceledException ex) when (ex.CancellationToken.IsCancellationRequested)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    _logger.LogDebug("Message receive cancelled for client {ConnectionId}", ConnectionId);
                }
                else
                {
                    _logger.LogWarning("Message receive timeout for client {ConnectionId}", ConnectionId);
                    await SendTimeoutErrorAsync("Message receive", 30_000, "Check network connection and retry");
                }
                return null;
            }
            catch (IOException ex) when (ex.InnerException is SocketException socketEx)
            {
                _logger.LogDebug("Client {ConnectionId} disconnected during message receive: {SocketError}",
                    ConnectionId, socketEx.SocketErrorCode);

                // Try to send network error if it's a recoverable error
                if (IsRecoverableSocketError(socketEx.SocketErrorCode))
                {
                    await SendNetworkErrorAsync(socketEx, shouldDisconnect: false);
                }
                return null;
            }
            catch (SocketException ex)
            {
                _logger.LogDebug("Client {ConnectionId} disconnected during message receive: {SocketError}",
                    ConnectionId, ex.SocketErrorCode);

                // Try to send network error if it's a recoverable error
                if (IsRecoverableSocketError(ex.SocketErrorCode))
                {
                    await SendNetworkErrorAsync(ex, shouldDisconnect: false);
                }
                return null;
            }
            catch (ObjectDisposedException)
            {
                _logger.LogDebug("Client {ConnectionId} disconnected - stream disposed", ConnectionId);
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error receiving message from client {ConnectionId}", ConnectionId);
                await SendErrorMessageAsync(MessageTypes.Error, "Message receive error");
                return null;
            }
        }

        internal override async UniTask SendMessageAsync(BaseMessage message)
        {
            if (_disposed || !_isConnected || _networkStream == null)
            {
                _logger.LogWarning("Attempted to send message to disconnected client {ConnectionId}", ConnectionId);
                return;
            }

            await _sendSemaphore.WaitAsync(_cancellationTokenSource.Token);
            try
            {
                uint messageType = message.GetMessageType();

                try
                {
                    await _networkStream.WriteMessageAsync(message, _cancellationTokenSource.Token);
                }
                catch (MessagePack.MessagePackSerializationException ex)
                {
                    _logger.LogError(ex, "MessagePack serialization failed for message type {MessageType} to client {ConnectionId}",
                        messageType, ConnectionId);

                    // Send error response instead of disconnecting
                    await SendSerializationErrorAsync(messageType);
                    return;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Unexpected serialization error for message type {MessageType} to client {ConnectionId}",
                        messageType, ConnectionId);
                    throw;
                }

                _logger.LogDebug("Sent message type {MessageType} to client {ConnectionId}",
                    messageType, ConnectionId);
            }
            catch (SocketException ex)
            {
                _logger.LogWarning(ex, "Socket error sending message to client {ConnectionId}: {ErrorCode}",
                    ConnectionId, ex.SocketErrorCode);

                // Try to send network error before disconnecting (if connection is still viable)
                if (IsRecoverableSocketError(ex.SocketErrorCode))
                {
                    await SendNetworkErrorAsync(ex, shouldDisconnect: false);
                }
                else
                {
                    await SendNetworkErrorAsync(ex, shouldDisconnect: true);
                    await DisconnectAsync();
                }
                throw;
            }
            catch (IOException ex) when (ex.InnerException is SocketException socketEx)
            {
                _logger.LogWarning(ex, "Network I/O error sending message to client {ConnectionId}: {SocketError}",
                    ConnectionId, socketEx.SocketErrorCode);

                // Try to send network error before disconnecting (if connection is still viable)
                if (IsRecoverableSocketError(socketEx.SocketErrorCode))
                {
                    await SendNetworkErrorAsync(socketEx, shouldDisconnect: false);
                }
                else
                {
                    await SendNetworkErrorAsync(socketEx, shouldDisconnect: true);
                    await DisconnectAsync();
                }
                throw;
            }
            catch (ObjectDisposedException)
            {
                _logger.LogDebug("Network stream disposed while sending message to client {ConnectionId}", ConnectionId);
                await DisconnectAsync();
                throw;
            }
            catch (OperationCanceledException) when (_cancellationTokenSource.Token.IsCancellationRequested)
            {
                _logger.LogDebug("Send operation cancelled for client {ConnectionId}", ConnectionId);
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error sending message to client {ConnectionId}", ConnectionId);
                await DisconnectAsync();
                throw;
            }
            finally
            {
                _sendSemaphore.Release();
            }
        }

        internal override async UniTask DisconnectAsync()
        {
            if (_disposed || !_isConnected)
                return;

            _logger.LogInformation("Disconnecting client {ConnectionId}", ConnectionId);

            _isConnected = false;
            _cancellationTokenSource.Cancel();

            try
            {
                _networkStream?.Close();
                _tcpClient?.Close();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error during client {ConnectionId} disconnection", ConnectionId);
            }

            // Wait for receive task to complete
            if (_receiveTask != null)
            {
                try
                {
                    await _receiveTask.Value;
                }
                catch (OperationCanceledException)
                {
                    // Expected when cancellation is requested
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error waiting for receive task completion for client {ConnectionId}", ConnectionId);
                }
            }

            // Raise disconnected event
            OnDisconnected();
        }

        public override async UniTask RejectConnectionAsync(ConnectionErrorTypes errorType, string errorText, bool shouldRetry, int retryDelayMs)
        {
            try
            {
                _logger.LogInformation("Rejecting connection from {RemoteEndPoint} - server at capacity",
                    TcpClient.Client.RemoteEndPoint);

                // Send message to tell user why it is rejected
                await SendConnectionErrorAsync(errorType, errorText, shouldRetry, retryDelayMs);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error rejecting connection");
            }
            finally
            {
                try
                {
                    TcpClient.Close();
                }
                catch (Exception closeEx)
                {
                    _logger.LogWarning(closeEx, "Error closing rejected TCP client");
                }
                TcpClient.Dispose();
            }
        }

        public override void Dispose()
        {
            if (_disposed)
                return;

            base.Dispose();

            _cancellationTokenSource?.Cancel();
            _sendSemaphore?.Dispose();
            _networkStream?.Dispose();
            _tcpClient?.Dispose();

            _logger.LogDebug("TcpClientConnection {ConnectionId} disposed", ConnectionId);
        }
    }
}
