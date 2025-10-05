using MessagePack;
using System;

namespace Insthync.SimpleNetworkManager.NET.Messages.Error
{
    /// <summary>
    /// Specialized error message for timeout-related failures.
    /// Provides context about what operation timed out and suggested recovery actions.
    /// </summary>
    [MessagePackObject]
    public class TimeoutErrorMessage : BaseMessage
    {
        /// <summary>
        /// Message type identifier for TimeoutErrorMessage
        /// </summary>
        public override uint GetMessageType() => MessageTypes.TimeoutError;

        /// <summary>
        /// Description of the operation that timed out
        /// </summary>
        [Key(0)]
        public string Operation { get; set; } = string.Empty;

        /// <summary>
        /// Timeout duration that was exceeded (in milliseconds)
        /// </summary>
        [Key(1)]
        public int TimeoutMs { get; set; }

        /// <summary>
        /// Timestamp when the timeout occurred (UTC)
        /// </summary>
        [Key(2)]
        public DateTime Timestamp { get; set; }

        /// <summary>
        /// Suggested action for the client to take
        /// </summary>
        [Key(3)]
        public string SuggestedAction { get; set; } = string.Empty;

        public TimeoutErrorMessage()
        {
            Timestamp = DateTime.UtcNow;
        }
    }
}
