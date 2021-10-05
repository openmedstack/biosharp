namespace OpenMedStack.BioSharp.Model.Bcl
{
    using System.Xml.Serialization;

    [XmlRoot(ElementName = "Run")]
    public class Run
    {
        [XmlElement(ElementName = "Flowcell")] public string Flowcell { get; set; } = null!;

        [XmlElement(ElementName = "Instrument")]
        public string Instrument { get; set; } = null!;

        [XmlElement(ElementName = "Date")] public string Date { get; set; } = null!;

        [XmlElement(ElementName = "Reads")] public Reads Reads { get; set; } = null!;

        [XmlElement(ElementName = "FlowcellLayout")]
        public FlowcellLayout FlowcellLayout { get; set; } = null!;

        [XmlElement(ElementName = "AlignToPhiX")]
        public AlignToPhiX? AlignToPhiX { get; set; }

        [XmlElement(ElementName = "ImageDimensions")]
        public ImageDimensions? ImageDimensions { get; set; }

        [XmlElement(ElementName = "ImageChannels")]
        public ImageChannels? ImageChannels { get; set; }

        [XmlAttribute(AttributeName = "Id")] public string Id { get; set; } = null!;

        [XmlAttribute(AttributeName = "Number")]
        public int Number { get; set; }
    }
}