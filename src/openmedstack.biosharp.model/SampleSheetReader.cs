using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace OpenMedStack.BioSharp.Model;

/// <summary>
/// Reads Illumina-format sample sheets (CSV with [Header], [Reads], [Settings], [Data] sections).
/// </summary>
public static class SampleSheetReader
{
    private enum Section { None, Header, Reads, Settings, Data }

    /// <summary>Reads a sample sheet from the given file path.</summary>
    public static async Task<SampleSheet> Read(string path)
    {
        await using var fs = File.OpenRead(path);
        return await Read(fs).ConfigureAwait(false);
    }

    /// <summary>Reads a sample sheet from the given stream.</summary>
    public static async Task<SampleSheet> Read(Stream stream)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);

        var header = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var reads = new List<int>();
        var settings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var samples = new List<SampleSheetSample>();
        var section = Section.None;
        string[]? dataColumns = null;

        while (true)
        {
            var line = await reader.ReadLineAsync().ConfigureAwait(false);
            if (line is null)
            {
                break;
            }

            // Skip empty lines
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var trimmed = line.Trim();

            // Section header?
            if (trimmed.StartsWith('['))
            {
                var sectionName = trimmed.Trim('[', ']', ',').Trim();
                section = sectionName.ToUpperInvariant() switch
                {
                    "HEADER" => Section.Header,
                    "READS" => Section.Reads,
                    "SETTINGS" => Section.Settings,
                    "DATA" => Section.Data,
                    _ => Section.None
                };
                dataColumns = null;
                continue;
            }

            switch (section)
            {
                case Section.Header:
                {
                    var parts = SplitCsv(trimmed.AsSpan());
                    header[parts[0]] = parts.Count switch
                    {
                        >= 2 => parts[1],
                        1 when !string.IsNullOrEmpty(parts[0]) => string.Empty,
                        _ => header[parts[0]]
                    };

                    break;
                }
                case Section.Reads:
                {
                    if (int.TryParse(trimmed.AsSpan().TrimEnd(','), out var readLen))
                    {
                        reads.Add(readLen);
                    }

                    break;
                }
                case Section.Settings:
                {
                    var parts = SplitCsv(trimmed.AsSpan());
                    if (parts.Count >= 2)
                    {
                        settings[parts[0]] = parts[1];
                    }

                    break;
                }
                case Section.Data:
                {
                    var parts = SplitCsv(trimmed.AsSpan());
                    if (dataColumns is null)
                    {
                        // First data line is the column header
                        dataColumns = parts.ToArray();
                        break;
                    }

                    var sample = ParseDataRow(dataColumns, parts);
                    samples.Add(sample);
                    break;
                }
            }
        }

        return new SampleSheet
        {
            Header = header,
            ReadLengths = reads,
            Settings = settings,
            Samples = samples
        };
    }

    private static SampleSheetSample ParseDataRow(string[] columns, List<string> values)
    {
        return new SampleSheetSample
        {
            Lane = GetInt("Lane"),
            SampleId = Get("Sample_ID"),
            SampleName = Get("Sample_Name"),
            SamplePlate = Get("Sample_Plate"),
            SampleWell = Get("Sample_Well"),
            I7IndexId = Get("I7_Index_ID"),
            Index1 = Get("index"),
            I5IndexId = Get("I5_Index_ID"),
            Index2 = Get("index2"),
            Project = Get("Sample_Project"),
            Description = Get("Description")
        };

        int GetInt(string name) => int.TryParse(Get(name), out var v) ? v : 0;

        string Get(string name)
        {
            var idx = Array.FindIndex(columns, c =>
                c.Equals(name, StringComparison.OrdinalIgnoreCase));
            return idx >= 0 && idx < values.Count ? values[idx] : string.Empty;
        }
    }

    private static List<string> SplitCsv(ReadOnlySpan<char> line)
    {
        var result = new List<string>();
        var remaining = line;

        while (true)
        {
            var comma = remaining.IndexOf(',');
            if (comma < 0)
            {
                result.Add(remaining.Trim().ToString());
                break;
            }
            result.Add(remaining[..comma].Trim().ToString());
            remaining = remaining[(comma + 1)..];
        }

        return result;
    }
}
