using System.Collections.Generic;

namespace OpenMedStack.BioSharp.Io.Vcf;

using System;
using System.Collections.Immutable;
using System.Linq;
using System.Text.RegularExpressions;
using Model;

public partial class VcfMetaReader
{
    public static IVariantMetaInformation Read(ReadOnlySpan<char> line)
    {
        var end = line.IndexOf('<');
        var kind = end == -1 ? null : line[2..(end - 1)];
        var trimmedLine = line[(end + 1)..]
            .TrimStart(['<', '#'])
            .TrimEnd(['>', ',']);
        var matchCollection = ContentRegex().Matches(trimmedLine.ToString());
        var contents = matchCollection.ToDictionary(
            x => x.Groups["key"].Value.TrimStart(','),
            x => x.Groups["value"].Value.Trim('"'));

        return kind switch
        {
            "contig" => new ContigFieldFormat(ImmutableDictionary<string, string>.Empty.AddRange(contents)),
            "INFO" => new InformationFieldFormat(contents["ID"], contents["Number"][0], contents["Type"],
                contents["Description"], contents.GetValueOrDefault("Source"), contents.GetValueOrDefault("Version")),
            "FILTER" => new FilterFieldFormat(contents["ID"], contents["Description"]),
            "FORMAT" => new IndividualFormatFieldFormat(contents["ID"], contents["Number"][0], contents["Type"],
                contents["Description"]),
            "ALT" => new AlternativeAlleleFieldFormat(Enum.Parse<AlternativeAlleleKind>(contents["ID"], true),
                contents["Description"]),
            _ => new KeyValueMetaInformation(contents.Keys.First(), contents.Values.First())
        };
    }

    [GeneratedRegex("(?<key>[^=]+)=(?<value>[^,]+|\\d,\\d|\".+\")(?=,[A-Z]|$)",
        RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex ContentRegex();
}
