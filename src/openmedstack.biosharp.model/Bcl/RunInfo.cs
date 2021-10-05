namespace OpenMedStack.BioSharp.Model.Bcl
{
    using System.Xml.Serialization;

    [XmlRoot(ElementName = "RunInfo")]
    public class RunInfo
    {
        [XmlElement(ElementName = "Run")] public Run Run { get; set; } = null!;

        [XmlAttribute(AttributeName = "Version")]
        public int Version { get; set; }
    }
}