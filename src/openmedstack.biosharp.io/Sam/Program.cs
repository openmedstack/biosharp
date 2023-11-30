namespace OpenMedStack.BioSharp.Io.Sam
{
    using System;
    using System.Linq;
    using System.Text;

    public record Program
    {
        internal Program(string id, string? programName = null, string? commandLine = null, string? previous = null, string? description = null, string? version = null)
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
            var parts = line[4..]
                .Split('\t', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(x => x.Split(':'))
                .ToDictionary(x => x[0], x => string.Join(':', x.Skip(1)));
            return new Program(
                parts["ID"],
                parts.TryGetValue("PN", out var pn) ? pn : null,
                parts.TryGetValue("CL", out var cl) ? cl : null,
                parts.TryGetValue("PP", out var pp) ? pp : null,
                parts.TryGetValue("DS", out var ds) ? ds : null,
                parts.TryGetValue("VN", out var vn) ? vn : null);
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
}
