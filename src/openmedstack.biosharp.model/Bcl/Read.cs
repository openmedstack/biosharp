namespace OpenMedStack.BioSharp.Model.Bcl
{
    using System.Xml.Serialization;

    [XmlRoot(ElementName = "Read")]
    public class Read
    {
        [XmlAttribute(AttributeName = "Number")]
        public int Number { get; set; }

        [XmlAttribute(AttributeName = "NumCycles")]
        public int NumCycles { get; set; }

        [XmlAttribute(AttributeName = "IsIndexedRead")]
        public string IsIndexedRead { get; set; } = "";
    }
}