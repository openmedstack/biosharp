namespace OpenMedStack.BioSharp.Io.Sam;

using System;
using System.Collections.Generic;
using System.Text;

public record Program
{
    internal Program(
        string id,
        string? programName = null,
        string? commandLine = null,
        string? previous = null,
        string? description = null,
        string? version = null)
    {
        Id = id;
        ProgramName = programName;
        CommandLine = commandLine;
        Previous = previous;
        Description = description;
        Version = version;
    }

    public string Id { get; }
    public string? ProgramName { get; }
    public string? CommandLine { get; }
    public string? Previous { get; }
    public string? Description { get; }
    public string? Version { get; }

    public static Program Parse(string line)
    {
        var span = line.AsSpan(4);
        Span<Range> tabRanges = stackalloc Range[20];
        var count = span.Split(tabRanges, '\t', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var parts = new Dictionary<string, string>(count);
        for (var i = 0; i < count; i++)
        {
            var field = span[tabRanges[i]];
            var colon = field.IndexOf(':');
            if (colon < 1)
            {
                continue;
            }

            parts[new string(field[..colon])] = new string(field[(colon + 1)..]);
        }
        return new Program(
            parts["ID"],
            parts.GetValueOrDefault("PN"),
            parts.GetValueOrDefault("CL"),
            parts.GetValueOrDefault("PP"),
            parts.GetValueOrDefault("DS"),
            parts.GetValueOrDefault("VN"));
    }

    /// <inheritdoc />
    public override string ToString()
    {
        if (string.IsNullOrWhiteSpace(Id))
        {
            return "";
        }

        var builder = new StringBuilder($"@PG\tID:{Id}");
        if (!string.IsNullOrWhiteSpace(ProgramName))
        {
            builder.Append($"\tPN:{ProgramName}");
        }

        if (!string.IsNullOrWhiteSpace(CommandLine))
        {
            builder.Append($"\tCL:{CommandLine}");
        }

        if (!string.IsNullOrWhiteSpace(Previous))
        {
            builder.Append($"\tPP:{Previous}");
        }

        if (!string.IsNullOrWhiteSpace(Description))
        {
            builder.Append($"\tDS:{Description}");
        }

        if (!string.IsNullOrWhiteSpace(Version))
        {
            builder.Append($"\tVN:{Version}");
        }

        return builder.ToString();
    }
}
