namespace OpenMedStack.BioSharp.Model.Bcl
{
    /// <summary>
    /// A class that holds the <see cref="BclData"/> provided by this parser.
    /// One BclData object is returned to IlluminaDataProvider per cluster and each first level array in bases and qualities represents a single read in that cluster.
    /// </summary>
    public class BclData //: IBaseData, IQualityData
    {
        public BclData(int[] outputLengths)
        {
            Bases = new byte[outputLengths.Length][];
            Qualities = new byte[outputLengths.Length][];

            for (var i = 0; i < outputLengths.Length; i++)
            {
                Bases[i] = new byte[outputLengths[i]];
                Qualities[i] = new byte[outputLengths[i]];
            }
        }

        public byte[][] Bases { get; }

        public byte[][] Qualities { get; }
    }
}
