namespace OpenMedStack.BioSharp.Io.Sam
{
    using System;
    using System.Linq;
    using System.Text;

    public record ReadGroup
    {
        internal ReadGroup(
            string id,
            string? barcodes = null,
            string? sequencingCenter = null,
            string? description = null,
            string? runTime = null,
            string? flowOrder = null,
            string? nucleotideBases = null,
            string? library = null,
            string? programs = null,
            string? predictedMedianInsertSize = null,
            string? platform = null,
            string? platformModel = null,
            string? platformUnit = null,
            string? sample = null)
        {
            Id = id;
            Barcodes = barcodes;
            SequencingCenter = sequencingCenter;
            Description = description;
            RunTime = runTime;
            FlowOrder = flowOrder;
            NucleotideBases = nucleotideBases;
            Library = library;
            Programs = programs;
            PredictedMedianInsertSize = predictedMedianInsertSize;
            Platform = platform;
            PlatformModel = platformModel;
            PlatformUnit = platformUnit;
            Sample = sample;
        }

        public string Id { get; }
        public string? Barcodes { get; }
        public string? SequencingCenter { get; }
        public string? Description { get; }
        public string? RunTime { get; }
        public string? FlowOrder { get; }
        public string? NucleotideBases { get; }
        public string? Library { get; }
        public string? Programs { get; }
        public string? PredictedMedianInsertSize { get; }
        public string? Platform { get; }
        public string? PlatformModel { get; }
        public string? PlatformUnit { get; }
        public string? Sample { get; }

        public static ReadGroup Parse(string line)
        {
            var parts = line[4..]
                .Split('\t', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(x => x.Split(':'))
                .ToDictionary(x => x[0], x => string.Join(':', x.Skip(1)));
            return new ReadGroup(
                parts["ID"],
                parts.TryGetValue("BC", out var bc) ? bc : null,
                parts.TryGetValue("CN", out var cn) ? cn : null,
                parts.TryGetValue("DS", out var ds) ? ds : null,
                parts.TryGetValue("DT", out var dt) ? dt : null,
                parts.TryGetValue("FO", out var fo) ? fo : null,
                parts.TryGetValue("KS", out var ks) ? ks : null,
                parts.TryGetValue("LB", out var lb) ? lb : null,
                parts.TryGetValue("PG", out var pg) ? pg : null,
                parts.TryGetValue("PI", out var pi) ? pi : null,
                parts.TryGetValue("PL", out var pl) ? pl : null,
                parts.TryGetValue("PM", out var pm) ? pm : null,
                parts.TryGetValue("PU", out var pu) ? pu : null,
                parts.TryGetValue("SM", out var sm) ? sm : null);
        }

        /// <inheritdoc />
        public override string ToString()
        {
            var builder = new StringBuilder($"@RG\tID:{Id}");
            if (Barcodes != null)
            {
                builder.Append($"\tBC:{Barcodes}");
            }
            if (SequencingCenter != null)
            {
                builder.Append($"\tCN:{SequencingCenter}");
            }
            if (Description != null)
            {
                builder.Append($"\tDS:{Description}");
            }
            if (RunTime != null)
            {
                builder.Append($"\tDT:{RunTime}");
            }
            if (FlowOrder != null)
            {
                builder.Append($"\tFO:{FlowOrder}");
            }
            if (NucleotideBases != null)
            {
                builder.Append($"\tKS:{NucleotideBases}");
            }
            if (Library != null)
            {
                builder.Append($"\tLB:{Library}");
            }
            if (Programs != null)
            {
                builder.Append($"\tPG:{Programs}");
            }
            if (PredictedMedianInsertSize != null)
            {
                builder.Append($"\tPI:{PredictedMedianInsertSize}");
            }
            if (Platform != null)
            {
                builder.Append($"\tPL:{Platform}");
            }
            if (PlatformModel != null)
            {
                builder.Append($"\tPM:{PlatformModel}");
            }
            if (PlatformUnit != null)
            {
                builder.Append($"\tPU:{PlatformUnit}");
            }
            if (Sample != null)
            {
                builder.Append($"\tSM:{Sample}");
            }
            return builder.ToString();
        }
    }
}
