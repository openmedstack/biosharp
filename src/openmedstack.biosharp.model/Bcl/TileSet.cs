namespace OpenMedStack.BioSharp.Model.Bcl
{
    using System.Xml.Serialization;

    [XmlRoot(ElementName = "TileSet")]
    public class TileSet
    {
        [XmlElement(ElementName = "Tiles")] public Tiles Tiles { get; set; } = null!;

        [XmlAttribute(AttributeName = "TileNamingConvention")]
        public string? TileNamingConvention { get; set; }
    }
}