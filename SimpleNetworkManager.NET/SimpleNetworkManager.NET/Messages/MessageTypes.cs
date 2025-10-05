namespace Insthync.SimpleNetworkManager.NET.Messages
{
    public partial class MessageTypes
    {
        // Error Messages (500-599)
        public const uint Error = 500;
        public const uint ConnectionError = 501;
        public const uint NetworkError = 502;
        public const uint TimeoutError = 503;
        public const uint UnknownMessageType = 504;
        public const uint SerializationError = 505;
    }
}
