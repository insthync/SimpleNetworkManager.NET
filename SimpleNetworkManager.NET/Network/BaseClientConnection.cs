using Insthync.SimpleNetworkManager.NET.Messages;
using Insthync.SimpleNetworkManager.NET.Messages.Error;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Insthync.SimpleNetworkManager.NET.Network
{
    public abstract class BaseClientConnection : IDisposable
    {
        private static int s_connectionIdCounter = 0;
        private static ConcurrentQueue<uint> s_unassignedConnectionIds = new ConcurrentQueue<uint>();

        private static int s_requestIdCounter = 0;

        protected readonly ILogger<BaseClientConnection> _logger;
        protected readonly ConcurrentDictionary<uint, TaskCompletionSource<BaseResponseMessage>> _pendingResponses;
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
            _pendingResponses = new ConcurrentDictionary<uint, TaskCompletionSource<BaseResponseMessage>>();
        }

        private static uint InterlockedIncrementUInt(ref int location)
        {
            Interlocked.Increment(ref location);
            return location < 0 ? (uint)(location + (long)uint.MaxValue + 1) : (uint)location;
        }

        private static uint GetNewConnectionId()
        {
            if (!s_unassignedConnectionIds.TryDequeue(out uint connectionId))
                connectionId = InterlockedIncrementUInt(ref s_connectionIdCounter);
            return connectionId;
        }

        private static uint GetNewRequestId()
        {
            uint requestId;
            do
            {
                requestId = InterlockedIncrementUInt(ref s_requestIdCounter);
            }
            while (requestId == 0);
            return requestId;
        }

        public void AssignConnectionId()
        {
            if (ConnectionId > 0)
                return;
            ConnectionId = GetNewConnectionId();
        }

        public void UnassignConnectionId()
        {
            if (ConnectionId > 0)
                s_unassignedConnectionIds.Enqueue(ConnectionId);
            ConnectionId = 0;
        }

        public void OnMessageReceived(byte[] buffer, int length)
        {
            MessageReceived?.Invoke(this, buffer, length);
        }

        public void OnDisconnected()
        {
            Disconnected?.Invoke(this);
        }

        /// <summary>
        /// Rejects a connection
        /// </summary>
        public abstract Task RejectConnectionAsync(ConnectionErrorTypes errorType, string errorText, bool shouldRetry, int retryDelayMs);

        /// <summary>
        /// Sends a message asynchronously to the connected client
        /// </summary>
        internal abstract Task SendMessageAsync(BaseMessage message);

        /// <summary>
        /// Disconnects the client gracefully
        /// </summary>
        internal abstract Task DisconnectAsync();

        internal async Task<TResponse> SendRequestAsync<TResponse>(BaseRequestMessage request, int timeoutMs = 10_000)
            where TResponse : BaseResponseMessage
        {
            uint requestId = GetNewRequestId();
            var responseCompletion = new TaskCompletionSource<BaseResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);

            if (!_pendingResponses.TryAdd(requestId, responseCompletion))
            {
                throw new InvalidOperationException($"Request ID collision detected for RequestId: {requestId}.");
            }

            request.RequestId = requestId;

            try
            {
                await SendMessageAsync(request);

                if (timeoutMs <= 0)
                    throw new TimeoutException($"Request timed out after {timeoutMs} milliseconds (RequestId: {requestId}).");

                var completedTask = await Task.WhenAny(responseCompletion.Task, Task.Delay(timeoutMs));
                if (completedTask != responseCompletion.Task)
                    throw new TimeoutException($"Request timed out after {timeoutMs} milliseconds (RequestId: {requestId}).");

                var response = await responseCompletion.Task;
                if (!(response is TResponse castedResponse))
                {
                    throw new InvalidOperationException($"Response type mismatch. Expected {typeof(TResponse).Name}, got {response.GetType().Name}");
                }

                return castedResponse;
            }
            finally
            {
                _pendingResponses.TryRemove(requestId, out _);
            }
        }

        internal void Responded(BaseResponseMessage? response)
        {
            if (response == null)
                return;
            uint requestId = response.RequestId;
            if (_pendingResponses.TryRemove(requestId, out var responseCompletion))
            {
                responseCompletion.TrySetResult(response);
            }
            else
            {
                _logger.LogDebug("Received response for unknown or timed-out RequestId: {RequestId}", requestId);
            }
        }

        protected void FailPendingResponses(Exception exception)
        {
            foreach (var pendingResponse in _pendingResponses)
            {
                if (_pendingResponses.TryRemove(pendingResponse.Key, out var responseCompletion))
                {
                    responseCompletion.TrySetException(exception);
                }
            }
        }

        /// <summary>
        /// Sends a serialization error message to the client
        /// </summary>
        public async Task SendSerializationErrorAsync(uint? failedMessageType)
        {
            string failedMessageTypeErrorText = failedMessageType.HasValue ? failedMessageType.Value.ToString() : "Unknown";
            await SendErrorMessageAsync(MessageTypes.SerializationError, $"Serialization processing failed: {failedMessageTypeErrorText}");
        }

        /// <summary>
        /// Sends a deserialization error message to the client
        /// </summary>
        public async Task SendDeserializationErrorAsync(uint? failedMessageType)
        {
            string failedMessageTypeErrorText = failedMessageType.HasValue ? failedMessageType.Value.ToString() : "Unknown";
            await SendErrorMessageAsync(MessageTypes.DeserializationError, $"Deserialization processing failed: {failedMessageTypeErrorText}");
        }

        /// <summary>
        /// Sends an unknown message type error to the client
        /// </summary>
        public async Task SendUnknownMessageTypeErrorAsync(uint? failedMessageType)
        {
            string failedMessageTypeErrorText = failedMessageType.HasValue ? failedMessageType.Value.ToString() : "Unknown";
            await SendErrorMessageAsync(MessageTypes.UnknownMessageType, $"Message type not supported: {failedMessageTypeErrorText}");
        }

        /// <summary>
        /// Sends a network error message to the client
        /// </summary>
        public async Task SendNetworkErrorAsync(SocketException socketEx, bool shouldDisconnect = true)
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
        public async Task SendConnectionErrorAsync(ConnectionErrorTypes errorType, string errorText, bool shouldRetry, int retryDelayMs = 10_000)
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
        public async Task SendTimeoutErrorAsync(string operation, int timeoutMs, string suggestedAction = "Retry the operation")
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
        public async Task SendErrorMessageAsync(uint errorCode, string errorText)
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
            FailPendingResponses(new ObjectDisposedException(GetType().Name));
            UnassignConnectionId();
        }
    }
}
