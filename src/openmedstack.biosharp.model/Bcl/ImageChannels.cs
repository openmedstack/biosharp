namespace OpenMedStack.BioSharp.Model.Bcl
{
    using System.Collections.Generic;
    using System.Xml.Serialization;

    [XmlRoot(ElementName = "ImageChannels")]
    public class ImageChannels
    {
        [XmlElement(ElementName = "Name")] public List<string> Name { get; set; } = null!;
    }
}