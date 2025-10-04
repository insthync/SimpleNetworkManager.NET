using MessagePack;
using System;

namespace Insthync.SimpleNetworkManager.NET.Messages
{
    /// <summary>
    /// Base class for all response messages that correspond to a request.
    /// Provides correlation ID for matching responses with requests.
    /// </summary>
    public abstract class BaseResponseMessage : BaseMessage
    {
        /// <summary>
        /// Correlation ID that matches the original request's RequestId
        /// </summary>
        [Key(0)]
        public Guid RequestId { get; set; }

        /// <summary>
        /// Indicates if the request was successful
        /// </summary>
        [Key(1)]
        public bool Success { get; set; }

        /// <summary>
        /// Error message if Success is false
        /// </summary>
        [Key(2)]
        public string? ErrorMessage { get; set; }

        /// <summary>
        /// Marks this response as an error
        /// </summary>
        /// <param name="errorMessage">Error description</param>
        public void SetError(string errorMessage)
        {
            Success = false;
            ErrorMessage = errorMessage;
        }
    }
}
