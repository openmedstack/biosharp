namespace OpenMedStack.BioSharp.Io;

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

internal static class StreamExtensions
{
    public static Task<Memory<byte>> FillBuffer(
        this Stream file,
        Memory<byte> buffer,
        CancellationToken cancellationToken = default)
    {
        return FillBuffer(file, buffer, false, cancellationToken);
    }

    public static async Task<Memory<byte>> FillBuffer(
        this Stream file,
        Memory<byte> buffer,
        bool allowEmpty = false,
        CancellationToken cancellationToken = default)
    {
        var totalRead = 0;
        while (totalRead < buffer.Length)
        {
            var read = await file.ReadAsync(buffer[totalRead..], cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                if (allowEmpty)
                {
                    return buffer[..totalRead];
                }
                throw new IOException("Nothing read. End of stream?");
            }
            totalRead += read;
        }

        return buffer;
    }
    public static async Task<byte[]> FillBuffer(
        this Stream file,
        int size,
        CancellationToken cancellationToken = default)
    {
        var buffer = new byte[size];
        var totalRead = 0;
        while (totalRead < buffer.Length)
        {
            totalRead += await file.ReadAsync(buffer.AsMemory()[totalRead..], cancellationToken).ConfigureAwait(false);
        }

        return buffer;
    }
}