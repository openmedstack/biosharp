namespace OpenMedStack.BioSharp.Io.Bam;

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

internal static class StreamExtensions
{
    public static async Task<Memory<byte>> FillBuffer(
        this Stream file,
        Memory<byte> buffer,
        CancellationToken cancellationToken)
    {
        var totalRead = 0;
        while (totalRead < buffer.Length)
        {
            totalRead += await file.ReadAsync(buffer[totalRead..], cancellationToken);
        }

        return buffer;
    }
}