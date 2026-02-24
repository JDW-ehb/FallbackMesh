using System;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace ZCL.Protocol.ZCSP.Transport
{
    internal static class Framing
    {
        /// <summary>
        /// Writes a length-prefixed frame to the stream.
        /// Frame format: [4-byte length][payload bytes]
        /// </summary>
        public static async Task WriteAsync(Stream stream, byte[] payload)
        {
            var lengthPrefix = BitConverter.GetBytes(payload.Length);

            await stream.WriteAsync(lengthPrefix);
            await stream.WriteAsync(payload);
        }

        /// <summary>
        /// Reads a full length-prefixed frame from the stream.
        /// Returns null if the connection is closed.
        /// </summary>
        public static async Task<byte[]?> ReadAsync(Stream stream)
        {
            var lengthBuffer = new byte[4];

            if (await ReadExactAsync(stream, lengthBuffer) == 0)
                return null;

            int payloadLength = BitConverter.ToInt32(lengthBuffer);

            // Hard safety limits (tune as needed)
            const int MaxFrameSize = 4 * 1024 * 1024; // 4 MB

            if (payloadLength <= 0 || payloadLength > MaxFrameSize)
                return null;

            var payload = new byte[payloadLength];

            if (await ReadExactAsync(stream, payload) == 0)
                return null;

            return payload;
        }

        /// <summary>
        /// Reads exactly buffer.Length bytes unless the connection closes.
        /// </summary>

        private static async Task<int> ReadExactAsync(
            Stream stream,
            byte[] buffer)
        {
            int offset = 0;

            while (offset < buffer.Length)
            {
                int read = await stream.ReadAsync(buffer.AsMemory(offset));
                if (read == 0)
                    return 0;

                offset += read;
            }

            return offset;
        }
    }
}
