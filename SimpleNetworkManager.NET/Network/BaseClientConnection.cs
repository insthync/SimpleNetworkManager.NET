using Cysharp.Threading.Tasks;
using Insthync.SimpleNetworkManager.NET.Messages;
using Insthync.SimpleNetworkManager.NET.Messages.Error;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Threading;

namespace Insthync.SimpleNetworkManager.NET.Network
{
    public abstract class BaseClientConnection : IDisposable
    {
        private static uint s_connectionIdCounter = 0;
        private static ConcurrentQueue<uint> s_unassignedConnectionIds = new ConcurrentQueue<uint>();

        protected readonly ILogger<BaseClientConnection> _logger;
        protected bool _disposed;

        public uint ConnectionId { get; protected set; }
        /// <summary>
        /// Indicates whether the client is connected
        /// </summary>
        public abstract bool IsConnected { get; }

        public event MessageReceivedHandler? MessageReceived;
        public event DisconnectedHandler? Disconnected;

        public BaseClientConnection(ILogger<BaseClientConnection> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public void AssignConnectionId()
        {
            if (ConnectionId > 0)
                return;
            if (!s_unassignedConnectionIds.TryDequeue(out uint connectionId))
                connectionId = Interlocked.Increment(ref s_connectionIdCounter);
            ConnectionId = connectionId;
        }

        public void UnassignConnectionId()
        {
            if (ConnectionId > 0)
                s_unassignedConnectionIds.Enqueue(ConnectionId);
        }

        public void OnMessageReceived(byte[] message)
        {
            MessageReceived?.Invoke(this, message);
        }

        public void OnDisconnected()
        {
            Disconnected?.Invoke(this);
        }

        /// <summary>
        /// Rejects a connection
        /// </summary>
        public abstract UniTask RejectConnectionAsync(ConnectionErrorTypes errorType, string errorText, bool shouldRetry, int retryDelayMs);

        /// <summary>
        /// Sends a message asynchronously to the connected client
        /// </summary>
        public abstract UniTask SendMessageAsync(BaseMessage message);

        /// <summary>
        /// Sends a serialization error message to the client
        /// </summary>
        public async UniTask SendSerializationErrorAsync(uint? failedMessageType)
        {
            string failedMessageTypeErrorText = failedMessageType.HasValue ? failedMessageType.Value.ToString() : "Unknow";
            await SendErrorMessageAsync(MessageTypes.SerializationError, $"Serialization processing failed: {failedMessageTypeErrorText}");
        }

        /// <summary>
        /// Sends a deserialization error message to the client
        /// </summary>
        public async UniTask SendDeserializationErrorAsync(uint? failedMessageType)
        {
            string failedMessageTypeErrorText = failedMessageType.HasValue ? failedMessageType.Value.ToString() : "Unknow";
            await SendErrorMessageAsync(MessageTypes.SerializationError, $"Deserialization processing failed: {failedMessageTypeErrorText}");
        }

        /// <summary>
        /// Sends an unknown message type error to the client
        /// </summary>
        public async UniTask SendUnknownMessageTypeErrorAsync(uint? failedMessageType)
        {
            string failedMessageTypeErrorText = failedMessageType.HasValue ? failedMessageType.Value.ToString() : "Unknow";
            await SendErrorMessageAsync(MessageTypes.UnknownMessageType, $"Message type not supported: {failedMessageTypeErrorText}");
        }

        /// <summary>
        /// Sends a network error message to the client
        /// </summary>
        public async UniTask SendNetworkErrorAsync(SocketException socketEx, bool shouldDisconnect = true)
        {
            try
            {
                var errorMessage = new NetworkErrorMessage
                {
                    SocketErrorCode = socketEx.SocketErrorCode,
                    ErrorText = $"Network error: {socketEx.SocketErrorCode} - {socketEx.Message}",
                    Timestamp = DateTime.UtcNow,
                    ShouldDisconnect = shouldDisconnect
                };
                await SendMessageAsync(errorMessage);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send network error to client {ConnectionId}", ConnectionId);
                // Fallback to basic error message
                await SendErrorMessageAsync(MessageTypes.NetworkError, $"Network error: {socketEx.SocketErrorCode} - {socketEx.Message}");
            }
        }

        /// <summary>
        /// Send a connection error message to the client
        /// </summary>
        public async UniTask SendConnectionErrorAsync(ConnectionErrorTypes errorType, string errorText, bool shouldRetry, int retryDelayMs = 10_000)
        {
            try
            {
                var errorMessage = new ConnectionErrorMessage
                {
                    ErrorType = errorType,
                    ErrorText = errorText,
                    Timestamp = DateTime.UtcNow,
                    ShouldRetry = shouldRetry,
                    RetryDelayMs = retryDelayMs,
                };
                await SendMessageAsync(errorMessage);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send timeout error to client {ConnectionId}", ConnectionId);
                // Fallback to basic error message
                await SendErrorMessageAsync(MessageTypes.ConnectionError, $"Connection error: {errorType}");
            }
        }

        /// <summary>
        /// Sends a timeout error message to the client
        /// </summary>
        public async UniTask SendTimeoutErrorAsync(string operation, int timeoutMs, string suggestedAction = "Retry the operation")
        {
            try
            {
                var errorMessage = new TimeoutErrorMessage
                {
                    Operation = operation,
                    TimeoutMs = timeoutMs,
                    Timestamp = DateTime.UtcNow,
                    SuggestedAction = suggestedAction
                };
                await SendMessageAsync(errorMessage);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send timeout error to client {ConnectionId}", ConnectionId);
                // Fallback to basic error message
                await SendErrorMessageAsync(MessageTypes.TimeoutError, $"Operation timeout: {operation}");
            }
        }

        /// <summary>
        /// Safely sends an error message without throwing exceptions
        /// </summary>
        public async UniTask SendErrorMessageAsync(uint errorCode, string errorText)
        {
            try
            {
                var errorMessage = new ErrorMessage
                {
                    ErrorType = errorCode,
                    ErrorText = errorText,
                    Timestamp = DateTime.UtcNow
                };
                await SendMessageAsync(errorMessage);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send error message to client {ConnectionId}: {ErrorText}", ConnectionId, errorText);
                // Don't throw to avoid cascading failures
            }
        }

        /// <summary>
        /// Determines if a socket error is potentially recoverable
        /// </summary>
        public static bool IsRecoverableSocketError(SocketError socketError)
        {
            return socketError switch
            {
                SocketError.WouldBlock => true,
                SocketError.TryAgain => true,
                SocketError.InProgress => true,
                SocketError.TimedOut => true,
                SocketError.NetworkUnreachable => true,
                SocketError.HostUnreachable => true,
                // Non-recoverable errors
                SocketError.ConnectionReset => false,
                SocketError.ConnectionAborted => false,
                SocketError.ConnectionRefused => false,
                SocketError.NotConnected => false,
                SocketError.Shutdown => false,
                SocketError.SocketError => false,
                _ => false // Default to non-recoverable for unknown errors
            };
        }

        /// <summary>
        /// Disposes the connection and releases resources
        /// </summary>
        public virtual void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            UnassignConnectionId();
        }
    }
}
