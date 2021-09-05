namespace OpenMedStack.BioSharp.Model
{
    /// <summary>
    /// Defines the HGVS Variant type.
    /// </summary>
    public record HgvsVariant
    {
        private HgvsVariant(string reference, int version, HgvsDescription description)
        {
            Reference = reference;
            Version = version;
            Description = description;
        }

        public string Reference { get; }

        public int Version { get; }

        public HgvsDescription Description { get; }

        public static HgvsVariant Parse(string input)
        {
            var dot = input.IndexOf('.');
            var colon = input.IndexOf(':');
            var reference = input[..dot];
            var version = int.Parse(input.Substring(dot + 1, colon - dot - 1));
            var description = HgvsDescription.Parse(input[(colon + 1)..]);

            return new HgvsVariant(reference, version, description);
        }
    }
}