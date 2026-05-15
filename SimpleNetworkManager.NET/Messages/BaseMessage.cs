using MessagePack;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;

namespace Insthync.SimpleNetworkManager.NET.Messages
{
    public abstract class BaseMessage
    {
        private static Dictionary<Type, BaseMessage> s_defaultInstances = new Dictionary<Type, BaseMessage>();
        public static BaseMessage GetDefaultInstance(Type type)
        {
            if (!typeof(BaseMessage).IsAssignableFrom(type))
            {
                throw new InvalidOperationException($"{type} is not a BaseMessage or subclass of it");
            }
            if (!s_defaultInstances.TryGetValue(type, out BaseMessage? result))
            {
                result = (BaseMessage?)Activator.CreateInstance(type);
                if (result == null)
                    return null!;
                s_defaultInstances[type] = result;
            }
            return result;
        }

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
        /// Serializes the message to binary format: size(int32 little-endian) + messageType(uint32 little-endian) + data(MessagePack)
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
            BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(0, 4), buffer.Length);

            // Write message type (uses the abstract function from concrete class)
            BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(4, 4), GetMessageType());

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
        public static byte[] ExtractMessageData(byte[] message, int messageLength, out uint messageType)
        {
            if (messageLength < 8)
                throw new ArgumentException("Data too short to contain message header");

            // Read total size (for validation)
            var totalSize = BinaryPrimitives.ReadInt32LittleEndian(message.AsSpan(0, 4));
            if (totalSize != messageLength)
                throw new ArgumentException("Message size mismatch");

            // Read message type
            messageType = BinaryPrimitives.ReadUInt32LittleEndian(message.AsSpan(4, 4));

            // Extract message data
            var messageData = new byte[messageLength - 8];
            Array.Copy(message, 8, messageData, 0, messageData.Length);

            return messageData;
        }
    }
}
