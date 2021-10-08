namespace OpenMedStack.BioSharp.Model.Bcl
{
    using System.Xml.Serialization;

    [XmlRoot(ElementName = "Read")]
    public class Read
    {
        private string _isIndexedRead = "N";
        public ReadType Type { get; set; }

        [XmlAttribute(AttributeName = "Number")]
        public int Number { get; set; }

        [XmlAttribute(AttributeName = "NumCycles")]
        public int NumCycles { get; set; }

        [XmlAttribute(AttributeName = "IsIndexedRead")]
        public string IsIndexedRead
        {
            get { return _isIndexedRead; }
            set
            {
                _isIndexedRead = value;
                if (_isIndexedRead == "Y" && Type == ReadType.S)
                {
                    Type = ReadType.B;
                }
            }
        }
    }
}