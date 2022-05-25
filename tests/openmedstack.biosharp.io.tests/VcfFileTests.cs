namespace OpenMedStack.BioSharp.Io.Tests
{
    using System.IO;
    using System.Linq;
    using System.Text;
    using System.Threading.Tasks;
    using Model;
    using Vcf;
    using Xunit;

    public class VcfFileTests
    {
        [Fact]
        public async Task CanParseGzFile()
        {
            var reader = new VcfFileReader(new VcfMetaReader());
            const string path = "D.4x.vcf.gz";
            await using var headeredContent = await reader.Read(path).ConfigureAwait(false);

            Assert.NotNull(headeredContent.Header);
            Assert.Equal(5655127, await headeredContent.CountAsync().ConfigureAwait(false));
        }

        [Fact]
        public async Task CanParseFile()
        {
            var content = @"##fileformat=VCFv4.2
##FORMAT=<ID=GT,Number=1,Type=Integer,Description=""Genotype"">
##FORMAT=<ID=GP,Number=G,Type=Float,Description=""Genotype Probabilities"">
##FORMAT=<ID=PL,Number=G,Type=Float,Description=""Phred-scaled Genotype Likelihoods"">
#CHROM	POS	ID	REF	ALT	QUAL	FILTER	INFO	FORMAT	SAMP001	SAMP002
20	1291018	rs11449	G	A	.	PASS	.	GT	0/0	0/1
20	2300608	rs84825	C	T	.	PASS	.	GT:GP	0/1:.	0/1:0.03,0.97,0
20	2301308	rs84823	T	G	.	PASS	.	GT:PL	./.:.	1/1:10,5,0";
            var file = new MemoryStream(Encoding.UTF8.GetBytes(content));
            await using var _ = file.ConfigureAwait(false);
            var reader = new VcfFileReader(new VcfMetaReader());
            await using var headeredContent = await reader.Read(file).ConfigureAwait(false);

            Assert.NotNull(headeredContent.Header);
            Assert.Equal(3, await headeredContent.CountAsync().ConfigureAwait(false));
        }

        [Fact]
        public void CanParseHeaders()
        {
            var reader = new VcfMetaReader();
            var file = @"##fileformat=VCFv4.2
##fileDate=20090805
##source=myImputationProgramV3.1
##reference=file:///seq/references/1000GenomesPilot-NCBI36.fasta
##contig=<ID=20,length=62435964,assembly=B36,md5=f126cdf8a6e0c7f379d618ff66beb2da,species=""Homo sapiens"",taxonomy=x>
##phasing=partial
##INFO=<ID=NS,Number=1,Type=Integer,Description=""Number of Samples With Data"">
##INFO=<ID=DP,Number=1,Type=Integer,Description=""Total Depth"">
##INFO=<ID=AF,Number=A,Type=Float,Description=""Allele Frequency"">
##INFO=<ID=AA,Number=1,Type=String,Description=""Ancestral Allele"">
##INFO=<ID=DB,Number=0,Type=Flag,Description=""dbSNP membership, build 129"">
##INFO=<ID=H2,Number=0,Type=Flag,Description=""HapMap2 membership"">
##FILTER=<ID=q10,Description=""Quality below 10"">
##FILTER=<ID=s50,Description=""Less than 50% of samples have data"">
##FORMAT=<ID=GT,Number=1,Type=String,Description=""Genotype"">
##FORMAT=<ID=GQ,Number=1,Type=Integer,Description=""Genotype Quality"">
##FORMAT=<ID=DP,Number=1,Type=Integer,Description=""Read Depth"">
##FORMAT=<ID=HQ,Number=2,Type=Integer,Description=""Haplotype Quality"">";

            var metaInformation = file.Split('\n').Select(x => x.Trim()).Select(reader.Read).ToList();

            Assert.Contains(metaInformation.OfType<KeyValueMetaInformation>(), x => x.Key == "fileformat");
        }
    }
}