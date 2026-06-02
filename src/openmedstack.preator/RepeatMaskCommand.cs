namespace OpenMedStack.Preator;

using System;
using System.Collections.Generic;
using System.CommandLine;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using OpenMedStack.BioSharp.Calculations.DeBruijn;
using OpenMedStack.BioSharp.Io.FastA;

internal static class RepeatMaskCommand
{
    internal static RepeatMaskOptions CreateOptions(ParseResult parseResult) =>
        new(
            FastaPath: parseResult.GetRequiredValue(PreatorCommandOptions.FastaRequiredOption),
            LibraryPath: parseResult.GetValue(PreatorCommandOptions.LibraryOption),
            MinMotifLength: parseResult.GetValue(PreatorCommandOptions.MinMotifLengthOption),
            OutputPath: parseResult.GetValue(PreatorCommandOptions.OutputOption)!,
            OutputPrefix: parseResult.GetValue(PreatorCommandOptions.OutputPrefixOption) ?? "masked");

    internal static async Task<int> Invoke(ParseResult parseResult, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            return await Run(CreateOptions(parseResult), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            await Console.Error.WriteLineAsync($"Repeat masking failed: {exception.Message}").ConfigureAwait(false);
            return 1;
        }
        finally
        {
            stopwatch.Stop();
            Console.WriteLine($"Total runtime: {stopwatch.Elapsed}");
        }
    }

    public static async Task<int> Run(RepeatMaskOptions options, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(options.FastaPath))
        {
            throw new FileNotFoundException("FASTA file not found.", options.FastaPath);
        }

        if (options.LibraryPath != null && !File.Exists(options.LibraryPath))
        {
            throw new FileNotFoundException("Repeat library file not found.", options.LibraryPath);
        }

        Directory.CreateDirectory(options.OutputPath);

        var outputFastaPath = Path.Combine(options.OutputPath, $"{options.OutputPrefix}.masked.fa");
        var summaryPath = Path.Combine(options.OutputPath, $"{options.OutputPrefix}.repeatmask-summary.json");

        Console.WriteLine($"Input FASTA   : {options.FastaPath}");
        Console.WriteLine($"Library       : {options.LibraryPath ?? "(none — no masking applied)"}");
        Console.WriteLine($"Min motif len : {options.MinMotifLength?.ToString() ?? "default"}");
        Console.WriteLine($"Output FASTA  : {outputFastaPath}");

        IList<RepeatElement> library;
        if (options.LibraryPath != null)
        {
            library = await RepeatMasker.LoadLibrary(options.LibraryPath, options.MinMotifLength)
                .ConfigureAwait(false);
            Console.WriteLine($"Library entries: {library.Count:N0}");
        }
        else
        {
            library = Array.Empty<RepeatElement>();
        }

        var reader = new FastAReader(NullLogger<FastAReader>.Instance);
        var sequences = options.FastaPath.EndsWith(".gz", StringComparison.OrdinalIgnoreCase)
            ? reader.ReadGz(options.FastaPath)
            : reader.Read(options.FastaPath);

        var totalBases = 0L;
        var totalMaskedBases = 0L;
        var totalRegions = 0;

        await using var outputFile = new StreamWriter(outputFastaPath, append: false, Encoding.ASCII);

        await foreach (var sequence in sequences.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var sequenceStr = new string(sequence.GetData().Span);
            totalBases += sequenceStr.Length;

            IList<MaskedRegion> regions;
            if (library.Count > 0)
            {
                regions = await RepeatMasker.MaskRepeats(sequenceStr, library).ConfigureAwait(false);
            }
            else
            {
                regions = Array.Empty<MaskedRegion>();
            }

            totalRegions += regions.Count;
            foreach (var region in regions)
            {
                totalMaskedBases += region.Length;
            }

            var masked = ApplyMask(sequenceStr, regions);

            await outputFile.WriteLineAsync($">{sequence.Id}").ConfigureAwait(false);
            for (var i = 0; i < masked.Length; i += 80)
            {
                var lineLen = Math.Min(80, masked.Length - i);
                await outputFile.WriteLineAsync(masked.AsMemory(i, lineLen), cancellationToken).ConfigureAwait(false);
            }
        }

        var maskedFraction = totalBases > 0 ? (double)totalMaskedBases / totalBases : 0.0;

        Console.WriteLine();
        Console.WriteLine($"Total bases     : {totalBases:N0}");
        Console.WriteLine($"Masked bases    : {totalMaskedBases:N0}");
        Console.WriteLine($"Masked regions  : {totalRegions:N0}");
        Console.WriteLine($"Masked fraction : {maskedFraction:P2}");
        Console.WriteLine($"Output FASTA    : {outputFastaPath}");
        Console.WriteLine($"Summary         : {summaryPath}");

        var summary = new RepeatMaskSummary
        {
            InputFasta = options.FastaPath,
            OutputFasta = outputFastaPath,
            LibraryPath = options.LibraryPath,
            TotalBases = totalBases,
            MaskedBases = totalMaskedBases,
            MaskedRegions = totalRegions,
            MaskedFraction = maskedFraction
        };

        var json = JsonSerializer.Serialize(summary, PreatorJsonContext.Default.RepeatMaskSummary);
        await File.WriteAllTextAsync(summaryPath, json, cancellationToken).ConfigureAwait(false);

        return 0;
    }

    private static string ApplyMask(string sequence, IList<MaskedRegion> regions)
    {
        if (regions.Count == 0)
        {
            return sequence;
        }

        var chars = sequence.ToCharArray();
        foreach (var region in regions)
        {
            for (var i = region.Start; i < region.End && i < chars.Length; i++)
            {
                chars[i] = 'N';
            }
        }

        return new string(chars);
    }
}
