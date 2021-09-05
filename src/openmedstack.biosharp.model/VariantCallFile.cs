namespace OpenMedStack.BioSharp.Model
{
    public class VariantCallFile
    {
        public VariantCallFile(IVariantMetaInformation[] meta, params VcfVariant[] entries)
        {
            Meta = meta;
            Entries = entries;
        }

        public IVariantMetaInformation[] Meta { get; }

        public VcfVariant[] Entries { get; }
    }
}
