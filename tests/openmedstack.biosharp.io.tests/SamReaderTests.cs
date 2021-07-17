namespace OpenMedStack.BioSharp.Io.Tests
{
    using System.IO;
    using System.Text;
    using System.Threading.Tasks;
    using Sam;
    using Xunit;

    public class SamReaderTests
    {
        [Fact]
        public async Task CanReadFile()
        {
            var file = @"@HD  VN:1.6  SO:coordinate
@SQ  SN:ref  LN:45
r001  99  ref  7  30  8M2I4M1D3M  =  37  39  TTAGATAAAGGATACTG  *
r002  0  ref  9  30  3S6M1P1I4M  *  0  0  AAAAGATAAGGATA  *
r003  0  ref  9  30  5S6M  *  0  0  GCCTAAGCTAA  *  SA:Z:ref,29,-,6H5M,17,0;
r004  0  ref  16  30  6M14N5M  *  0  0  ATAGCTTCAGC  *
r003  2064  ref  29  17  6H5M  *  0  0  TAGGC  *  SA:Z:ref,9,+,5S6M,30,1;
r001  147  ref  37  30  9M  =  7  -39  CAGCGGCAT  *  NM:i:1
".Replace("  ", "\t");

            await using var ms = new MemoryStream(Encoding.UTF8.GetBytes(file));
            var reader = new SamReader();
            var definition = await reader.Read(ms).ConfigureAwait(false);

            Assert.Equal(6, definition.AlignmentSections.Length);
        }
    }
}