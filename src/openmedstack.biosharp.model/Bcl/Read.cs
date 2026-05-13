namespace OpenMedStack.BioSharp.Model.Bcl;

using System.Xml.Serialization;

[XmlRoot(ElementName = "Read")]
public class Read
{
    public ReadType Type { get; set; }

    [XmlAttribute(AttributeName = "Number")]
    public int Number { get; set; }

    [XmlAttribute(AttributeName = "NumCycles")]
    public int NumCycles { get; set; }

    [XmlAttribute(AttributeName = "IsIndexedRead")]
    public string IsIndexedRead
    {
        get { return field; }
        set
        {
            field = value;
            if (field == "Y" && Type == ReadType.S)
            {
                Type = ReadType.B;
            }
        }
    } = "N";
}
