using MessagePack;
using System;
using System.Net.Sockets;

namespace Insthync.SimpleNetworkManager.NET.Messages.Error
{
    /// <summary>
    /// Specialized error message for network-related failures.
    /// Provides additional context about network errors for debugging.
    /// </summary>
    [MessagePackObject]
    public class NetworkErrorMessage : BaseMessage
    {
        /// <summary>
        /// Message type identifier for NetworkErrorMessage
        /// </summary>
        public override uint GetMessageType() => MessageTypes.NetworkError;

        /// <summary>
        /// Specific socket error code
        /// </summary>
        [Key(0)]
        public SocketError SocketErrorCode { get; set; }

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
        /// Whether the connection should be terminated due to this error
        /// </summary>
        [Key(3)]
        public bool ShouldDisconnect { get; set; }

        public NetworkErrorMessage()
        {
            Timestamp = DateTime.UtcNow;
        }
    }
}
