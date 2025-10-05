using MessagePack;
using System;

namespace Insthync.SimpleNetworkManager.NET.Messages
{
    public abstract class BaseMessage
    {
        /// <summary>
        /// Unique identifier for the message type, used for routing messages to appropriate handlers.
        /// This is handled separately in the message header, not in MessagePack serialization.
        /// Each concrete message class must define its own message type.
        /// </summary>
        public abstract uint GetMessageType();

        public BaseMessage() { }

        /// <summary>
        /// Gets MessagePack serialization options for this message type
        /// </summary>
        public virtual MessagePackSerializerOptions GetMessagePackOptions()
        {
            return MessagePackSerializerOptions.Standard;
        }

        /// <summary>
        /// Get serialized MessagePack data
        /// </summary>
        /// <returns></returns>
        protected virtual byte[] SerializeData()
        {
            return MessagePackSerializer.Serialize(GetType(), this, GetMessagePackOptions());
        }

        /// <summary>
        /// Serializes the message to binary format: size(int) + messageType(uint) + data(MessagePack)
        /// Uses the abstract GetMessageType() function from the concrete class.
        /// </summary>
        /// <returns>Binary representation of the message</returns>
        public byte[] Serialize()
        {
            // Serialize the concrete message type using MessagePack
            var messageData = SerializeData();

            // Create the final buffer: size(4 bytes) + messageType(4 bytes) + data
            var buffer = new byte[8 + messageData.Length];

            // Write total size (including header)
            BitConverter.GetBytes(buffer.Length).CopyTo(buffer, 0);

            // Write message type (uses the abstract function from concrete class)
            BitConverter.GetBytes(GetMessageType()).CopyTo(buffer, 4);

            // Write message data
            messageData.CopyTo(buffer, 8);

            return buffer;
        }

        /// <summary>
        /// Deserializes a message from binary format
        /// </summary>
        /// <param name="message">Binary data containing the message</param>
        /// <param name="messageType">The message type extracted from the header</param>
        /// <returns>Deserialized message data (without header)</returns>
        public static byte[] ExtractMessageData(byte[] message, out uint messageType)
        {
            if (message.Length < 8)
                throw new ArgumentException("Data too short to contain message header");

            // Read total size (for validation)
            var totalSize = BitConverter.ToInt32(message, 0);
            if (totalSize != message.Length)
                throw new ArgumentException("Message size mismatch");

            // Read message type
            messageType = BitConverter.ToUInt32(message, 4);

            // Extract message data
            var messageData = new byte[message.Length - 8];
            Array.Copy(message, 8, messageData, 0, messageData.Length);

            return messageData;
        }
    }
}
