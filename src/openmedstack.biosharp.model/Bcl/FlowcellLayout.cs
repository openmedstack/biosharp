namespace OpenMedStack.BioSharp.Model.Bcl
{
    using System.Xml.Serialization;

    [XmlRoot(ElementName = "FlowcellLayout")]
    public class FlowcellLayout
    {
        [XmlElement(ElementName = "TileSet")] public TileSet TileSet { get; set; } = null!;

        [XmlAttribute(AttributeName = "LaneCount")]
        public sbyte LaneCount { get; set; }

        [XmlAttribute(AttributeName = "SurfaceCount")]
        public sbyte SurfaceCount { get; set; }

        [XmlAttribute(AttributeName = "SwathCount")]
        public sbyte SwathCount { get; set; }

        [XmlAttribute(AttributeName = "TileCount")]
        public int TileCount { get; set; }

        [XmlAttribute(AttributeName = "FlowcellSide")]
        public string? FlowcellSide { get; set; }
    }
}