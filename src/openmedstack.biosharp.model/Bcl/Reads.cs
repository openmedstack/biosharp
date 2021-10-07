namespace OpenMedStack.BioSharp.Model.Bcl
{
    using System.Collections.Generic;
    using System.Xml.Serialization;

    [XmlRoot(ElementName = "Reads")]
    public class Reads
    {
        [XmlElement(ElementName = "Read")] public List<Read>? Read { get; set; }
    }
}