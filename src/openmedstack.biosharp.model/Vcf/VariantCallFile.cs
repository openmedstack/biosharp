namespace OpenMedStack.BioSharp.Model.Vcf
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
