namespace OpenMedStack.BioSharp.Model.Bcl
{
    using System.Xml.Serialization;

    [XmlRoot(ElementName = "ImageDimensions")]
    public class ImageDimensions
    {
        [XmlAttribute(AttributeName = "Width")]
        public int Width { get; set; }

        [XmlAttribute(AttributeName = "Height")]
        public int Height { get; set; }
    }
}