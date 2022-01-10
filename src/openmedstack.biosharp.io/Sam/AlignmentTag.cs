namespace OpenMedStack.BioSharp.Io.Sam;

using System;
using System.IO;

public class AlignmentTag
{
    public AlignmentTag(string key, object value)
        : this(
            key,
            value switch
            {
                char => 'A',
                int => 'i',
                uint => 'I',
                float => 'f',
                string => 'Z',
                char[] { Length: 2 } => 'H',
                sbyte => 'c',
                byte => 'C',
                short => 's',
                ushort => 'S',
                _ => throw new InvalidDataException("Invalid tag value")
            },
            value)
    {
    }

    internal AlignmentTag(string key, char type, object value)
    {
        Key = key[..2];
        Type = type;
        Value = value;
    }

    private AlignmentTag(string key, char type, string value)
    {
        Key = key;
        Type = type;
        Value = type switch
        {
            'A' => value[0],
            'i' => int.Parse(value),
            'I' => uint.Parse(value),
            'f' => float.Parse(value),
            'Z' => value,
            'H' => value[..2].ToCharArray(),
            'c' => sbyte.Parse(value),
            'C' => byte.Parse(value),
            's' => short.Parse(value),
            'S' => ushort.Parse(value),
            _ => throw new InvalidDataException("Invalid tag value")
        };
    }

    public string Key { get; }

    public char Type { get; }

    public object Value { get; }

    public static AlignmentTag Parse(string tag)
    {
        var parts = tag.Split(':', StringSplitOptions.TrimEntries);
        return new AlignmentTag(parts[0][..2], parts[1][0], parts[2]);
    }

    /// <inheritdoc />
    public override string ToString()
    {
        return $"{Key}:{Type}:{(Type == 'H' ? ($"{string.Join("", (char[])Value)}") : Value)}";
    }

    public int GetSize()
    {
        return 3
               + Type switch
               {
                   'A' => 1,
                   'i' => 4,
                   'I' => 4,
                   'f' => 4,
                   'Z' => Value.ToString()!.Length + 1,
                   'H' => 2,
                   'c' => 1,
                   'C' => 1,
                   's' => 2,
                   'S' => 2,
                   _ => 0
               };
    }
}
