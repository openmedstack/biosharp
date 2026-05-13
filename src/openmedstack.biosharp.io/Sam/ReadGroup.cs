namespace OpenMedStack.BioSharp.Io.Sam;

using System;
using System.Collections.Generic;
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
        return new ReadGroup(
            parts["ID"],
            parts.GetValueOrDefault("BC"),
            parts.GetValueOrDefault("CN"),
            parts.GetValueOrDefault("DS"),
            parts.GetValueOrDefault("DT"),
            parts.GetValueOrDefault("FO"),
            parts.GetValueOrDefault("KS"),
            parts.GetValueOrDefault("LB"),
            parts.GetValueOrDefault("PG"),
            parts.GetValueOrDefault("PI"),
            parts.GetValueOrDefault("PL"),
            parts.GetValueOrDefault("PM"),
            parts.GetValueOrDefault("PU"),
            parts.GetValueOrDefault("SM"));
    }

    /// <inheritdoc />
    public override string ToString()
    {
        if (string.IsNullOrWhiteSpace(Id))
        {
            return "";
        }

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
