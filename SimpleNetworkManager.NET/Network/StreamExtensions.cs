using Cysharp.Threading.Tasks;
using Insthync.SimpleNetworkManager.NET.Messages;
using System;
using System.IO;
using System.Threading;

namespace Insthync.SimpleNetworkManager.NET.Network
{
    public static class StreamExtensions
    {
        /// <summary>
        /// Reads exactly the specified number of bytes from the stream
        /// </summary>
        public static async UniTask<int> ReadExactAsync(this Stream stream, byte[] buffer, int count, CancellationToken cancellationToken, int offset = 0)
        {
            if (stream == null)
                return 0;

            int totalBytesRead = 0;
            while (totalBytesRead < count && !cancellationToken.IsCancellationRequested)
            {
                var bytesRead = await stream.ReadAsync(
                    buffer, offset + totalBytesRead, count - totalBytesRead, cancellationToken);

                if (bytesRead == 0)
                    break; // Connection closed

                totalBytesRead += bytesRead;
            }
            return totalBytesRead;
        }

        /// <summary>
        /// Read message from the stream
        /// </summary>
        /// <param name="stream"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        /// <exception cref="InvalidMessageSizeException"></exception>
        public static async UniTask<byte[]?> ReadMessageAsync(this Stream stream, CancellationToken cancellationToken)
        {
            if (stream == null)
                return null;

            // Read message size (4 bytes)
            var sizeBuffer = new byte[4];
            var bytesRead = await stream.ReadExactAsync(sizeBuffer, 4, cancellationToken);
            if (bytesRead == 0 || bytesRead < 4)
                return null; // Connection closed

            var dataSize = BitConverter.ToInt32(sizeBuffer, 0);
            int minSize = 8;
            int maxSize = 1024 * 1024; // 1 MB
            if (dataSize < minSize || dataSize > maxSize)
            {
                throw new InvalidMessageSizeException()
                {
                    Size = dataSize,
                    MinSize = minSize,
                    MaxSize = maxSize
                };
            }

            // Read the complete message (including the size we already read)
            var dataBuffer = new byte[dataSize];
            sizeBuffer.CopyTo(dataBuffer, 0);

            // Already read 4 bytes for message size, so decrease by 4
            var remainingBytes = dataSize - 4;
            // Read next bytes by remaining bytes, skip 4 bytes (message size which already copied above)
            bytesRead = await stream.ReadExactAsync(dataBuffer, remainingBytes, cancellationToken, 4);
            if (bytesRead != remainingBytes)
                return null; // Connection closed

            return dataBuffer;
        }

        /// <summary>
        /// Reads exactly the specified number of bytes from the stream
        /// </summary>
        public static int ReadExact(this Stream stream, byte[] buffer, int count, int offset = 0)
        {
            if (stream == null)
                return 0;

            int totalBytesRead = 0;
            while (totalBytesRead < count)
            {
                var bytesRead = stream.Read(
                    buffer, offset + totalBytesRead, count - totalBytesRead);

                if (bytesRead == 0)
                    break; // Connection closed

                totalBytesRead += bytesRead;
            }
            return totalBytesRead;
        }

        /// <summary>
        /// Read message from the stream
        /// </summary>
        /// <param name="stream"></param>
        /// <returns></returns>
        /// <exception cref="InvalidMessageSizeException"></exception>
        public static byte[]? ReadMessage(this Stream stream)
        {
            if (stream == null)
                return null;

            // Read message size (4 bytes)
            var sizeBuffer = new byte[4];
            var bytesRead = stream.ReadExact(sizeBuffer, 4);
            if (bytesRead == 0 || bytesRead < 4)
                return null; // Connection closed

            var dataSize = BitConverter.ToInt32(sizeBuffer, 0);
            int minSize = 8;
            int maxSize = 1024 * 1024; // 1 MB
            if (dataSize < minSize || dataSize > maxSize)
            {
                throw new InvalidMessageSizeException()
                {
                    Size = dataSize,
                    MinSize = minSize,
                    MaxSize = maxSize
                };
            }

            // Read the complete message (including the size we already read)
            var dataBuffer = new byte[dataSize];
            sizeBuffer.CopyTo(dataBuffer, 0);

            // Already read 4 bytes for message size, so decrease by 4
            var remainingBytes = dataSize - 4;
            // Read next bytes by remaining bytes, skip 4 bytes (message size which already copied above)
            bytesRead = stream.ReadExact(dataBuffer, remainingBytes, 4);
            if (bytesRead != remainingBytes)
                return null; // Connection closed

            return dataBuffer;
        }

        public static async UniTask WriteMessageAsync<T>(this Stream stream, T message, CancellationToken cancellationToken)
            where T : BaseMessage
        {
            if (stream == null)
                return;

            uint messageType = message.GetMessageType();
            byte[] serializedData;
            try
            {
                serializedData = message.Serialize();
            }
            catch (MessagePack.MessagePackSerializationException)
            {
                throw;
            }
            catch (Exception)
            {
                throw;
            }

            await stream.WriteAsync(serializedData, cancellationToken);
            await stream.FlushAsync(cancellationToken);
        }

        public static void WriteMessage<T>(this Stream stream, T message)
            where T : BaseMessage
        {
            if (stream == null)
                return;

            uint messageType = message.GetMessageType();
            byte[] serializedData;
            try
            {
                serializedData = message.Serialize();
            }
            catch (MessagePack.MessagePackSerializationException)
            {
                throw;
            }
            catch (Exception)
            {
                throw;
            }

            stream.Write(serializedData);
            stream.Flush();
        }
    }
}
