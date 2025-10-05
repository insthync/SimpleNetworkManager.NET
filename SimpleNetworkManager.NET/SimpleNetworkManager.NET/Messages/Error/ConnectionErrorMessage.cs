using MessagePack;
using System;

namespace Insthync.SimpleNetworkManager.NET.Messages.Error
{
    /// <summary>
    /// Specialized error message for connection-related failures.
    /// Provides context about connection establishment and lifecycle errors.
    /// </summary>
    [MessagePackObject]
    public class ConnectionErrorMessage : BaseMessage
    {
        /// <summary>
        /// Message type identifier for ConnectionErrorMessage
        /// </summary>
        public override uint GetMessageType() => MessageTypes.ConnectionError;

        /// <summary>
        /// Type of connection error (Establishment, Authentication, Capacity, etc.)
        /// </summary>
        [Key(0)]
        public ConnectionErrorTypes ErrorType { get; set; }

        /// <summary>
        /// Human-readable error description
        /// </summary>
        [Key(1)]
        public string ErrorText { get; set; } = string.Empty;

        /// <summary>
        /// Timestamp when the error occurred (UTC)
        /// </summary>
        [Key(2)]
        public DateTime Timestamp { get; set; }

        /// <summary>
        /// Whether the client should attempt to reconnect
        /// </summary>
        [Key(3)]
        public bool ShouldRetry { get; set; }

        /// <summary>
        /// Suggested delay before retry attempt (in milliseconds)
        /// </summary>
        [Key(4)]
        public int RetryDelayMs { get; set; }

        public ConnectionErrorMessage()
        {
            Timestamp = DateTime.UtcNow;
        }
    }
}
