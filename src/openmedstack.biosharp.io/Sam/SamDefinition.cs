namespace OpenMedStack.BioSharp.Io.Sam
{
    public class SamDefinition
    {
        public FileMetadata Hd { get; set; }

        public ReferenceSequence Sq { get; set; }

        public ReadGroup Rg { get; set; }

        public Program Pg { get; set; }
    }
}