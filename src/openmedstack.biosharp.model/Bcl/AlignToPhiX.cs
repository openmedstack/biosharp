namespace OpenMedStack.BioSharp.Model.Bcl
{
    using System.Xml.Serialization;

    [XmlRoot(ElementName = "AlignToPhiX")]
    public class AlignToPhiX
    {
        [XmlElement(ElementName = "Lane")]
        public string? Lane { get; set; }
    }
}