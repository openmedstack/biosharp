namespace OpenMedStack.BioSharp.Model
{
    public class VariantCallFile
    {
        public VariantCallFile(IVariantMetaInformation[] meta, params Variant[] entries)
        {
            Meta = meta;
            Entries = entries;
        }

        public IVariantMetaInformation[] Meta { get; }

        public Variant[] Entries { get; }
    }
}
