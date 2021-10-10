namespace OpenMedStack.BioSharp.Model.Bcl
{
    using System.Collections.Generic;
    using System.Xml.Serialization;

    [XmlRoot(ElementName = "Tiles")]
    public class Tiles
    {
        [XmlElement(ElementName = "Tile")] public List<string>? Tile { get; set; }
    }
}