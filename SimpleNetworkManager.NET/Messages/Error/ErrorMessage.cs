using MessagePack;
using System;

namespace Insthync.SimpleNetworkManager.NET.Messages.Error
{
    /// <summary>
    /// Message sent to client when an error occurs during message processing.
    /// Contains error details and timestamp for debugging.
    /// </summary>
    [MessagePackObject]
    public class ErrorMessage : BaseMessage
    {
        /// <summary>
        /// Message type identifier for ErrorMessage
        /// </summary>
        public override uint GetMessageType() => MessageTypes.Error;

        /// <summary>
        /// Specific error type for categorizing the error type
        /// </summary>
        [Key(0)]
        public uint ErrorType { get; set; }

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

        public ErrorMessage()
        {
            Timestamp = DateTime.UtcNow;
        }
    }
}
