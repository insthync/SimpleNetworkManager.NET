using MessagePack;
using System;

namespace Insthync.SimpleNetworkManager.NET.Messages
{
    /// <summary>
    /// Base class for all request messages that expect a response.
    /// Provides correlation ID for matching requests with responses.
    /// Reserved keys:
    /// 0 - RequestId (Guid)
    /// </summary>
    public abstract class BaseRequestMessage : BaseMessage
    {
        /// <summary>
        /// Unique identifier for correlating this request with its response.
        /// Generated automatically when the request is created.
        /// </summary>
        [Key(0)]
        public Guid RequestId { get; internal set; }
    }
}
