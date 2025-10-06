using Cysharp.Threading.Tasks;
using Insthync.SimpleNetworkManager.NET.Messages;
using System;
using System.Buffers;
using System.IO;
using System.Threading;

namespace Insthync.SimpleNetworkManager.NET.Network
{
    public static class StreamExtensions
    {
        /// <summary>
        /// Reads exactly the specified number of bytes from the stream
        /// </summary>
        public static async UniTask<int> ReadExactAsync(this Stream stream, Memory<byte> buffer, int count, CancellationToken cancellationToken)
        {
            int totalBytesRead = 0;
            while (totalBytesRead < count && !cancellationToken.IsCancellationRequested)
            {
                var slice = buffer.Slice(totalBytesRead, count - totalBytesRead);
                int bytesRead = await stream.ReadAsync(slice, cancellationToken);
                if (bytesRead == 0)
                    break; // Connection closed
                totalBytesRead += bytesRead;
            }
            return totalBytesRead;
        }

        /// <summary>
        /// Read message from the stream, message buffer should be returned to array pool after use
        /// </summary>
        /// <param name="stream"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        /// <exception cref="InvalidMessageSizeException"></exception>
        public static async UniTask<(byte[] buffer, int length)?> ReadMessageAsync(this Stream stream, CancellationToken cancellationToken)
        {
            if (stream == null)
                return null;

            byte[] sizeBuffer = ArrayPool<byte>.Shared.Rent(4);
            try
            {
                var sizeMemory = sizeBuffer.AsMemory(0, 4);
                int bytesRead = await stream.ReadExactAsync(sizeMemory, 4, cancellationToken);
                if (bytesRead < 4)
                    return null;

                int dataSize = BitConverter.ToInt32(sizeBuffer, 0);
                int minSize = 8;
                int maxSize = 1024 * 1024; // 1 MB limit
                if (dataSize < minSize || dataSize > maxSize)
                    throw new InvalidMessageSizeException() { Size = dataSize, MinSize = minSize, MaxSize = maxSize };

                // Rent buffer for the full message
                byte[] dataBuffer = ArrayPool<byte>.Shared.Rent(dataSize);
                Memory<byte> dataMemory = dataBuffer.AsMemory(0, dataSize);

                // Copy size header into data buffer
                sizeMemory.Span.CopyTo(dataMemory.Span);

                // Read the rest of the message body directly into the span
                int remainingBytes = dataSize - 4;
                bytesRead = await stream.ReadExactAsync(dataMemory.Slice(4, remainingBytes), remainingBytes, cancellationToken);
                if (bytesRead != remainingBytes)
                {
                    ArrayPool<byte>.Shared.Return(dataBuffer);
                    return null; // Connection closed
                }

                return (dataBuffer, dataSize);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(sizeBuffer);
            }
        }

        /// <summary>
        /// Reads exactly the specified number of bytes from the stream
        /// </summary>
        public static int ReadExact(this Stream stream, Span<byte> buffer, int count, CancellationToken cancellationToken)
        {
            int totalBytesRead = 0;
            while (totalBytesRead < count && !cancellationToken.IsCancellationRequested)
            {
                int bytesRead = stream.Read(buffer.Slice(totalBytesRead, count - totalBytesRead));
                if (bytesRead == 0)
                    break; // Connection closed
                totalBytesRead += bytesRead;
            }
            return totalBytesRead;
        }

        /// <summary>
        /// Read message from the stream, message buffer should be returned to array pool after use
        /// </summary>
        /// <param name="stream"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        /// <exception cref="InvalidMessageSizeException"></exception>
        public static (byte[] buffer, int length)? ReadMessage(this Stream stream, CancellationToken cancellationToken)
        {
            if (stream == null)
                return null;

            byte[] sizeBuffer = ArrayPool<byte>.Shared.Rent(4);
            try
            {
                Span<byte> sizeSpan = sizeBuffer.AsSpan(0, 4);
                int bytesRead = stream.ReadExact(sizeSpan, 4, cancellationToken);
                if (bytesRead < 4)
                    return null; // Connection closed

                int dataSize = BitConverter.ToInt32(sizeBuffer, 0);
                const int minSize = 8;
                const int maxSize = 1024 * 1024; // 1 MB limit
                if (dataSize < minSize || dataSize > maxSize)
                    throw new InvalidMessageSizeException() { Size = dataSize, MinSize = minSize, MaxSize = maxSize };

                // Rent buffer for the full message
                byte[] dataBuffer = ArrayPool<byte>.Shared.Rent(dataSize);
                Span<byte> dataSpan = dataBuffer.AsSpan(0, dataSize);

                // Copy size header into data buffer
                sizeSpan.CopyTo(dataSpan);

                // Read the rest of the message body directly into the span
                int remainingBytes = dataSize - 4;
                bytesRead = stream.ReadExact(dataSpan.Slice(4, remainingBytes), remainingBytes, cancellationToken);
                if (bytesRead != remainingBytes)
                {
                    ArrayPool<byte>.Shared.Return(dataBuffer);
                    return null; // Connection closed
                }

                return (dataBuffer, dataSize);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(sizeBuffer);
            }
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
