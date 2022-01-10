namespace OpenMedStack.BioSharp.Io.Tests
{
    using System.IO;
    using System.Text;
    using System.Threading.Tasks;
    using Microsoft.Extensions.Logging.Abstractions;
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

            var ms = new MemoryStream(Encoding.UTF8.GetBytes(file));
            await using var _ = ms.ConfigureAwait(false);
            var reader = new SamReader(NullLogger.Instance);
            var definition = await reader.Read(ms).ConfigureAwait(false);

            Assert.Equal(6, definition.AlignmentSections.Length);
        }
        [Fact]
        public async Task CanReadFile2()
        {
            var file = @"@HD	VN:1.0	SO:coordinate
@SQ	SN:1	LN:249250621	AS:NCBI37	UR:file:/data/local/ref/GATK/human_g1k_v37.fasta	M5:1b22b98cdeb4a9304cb5d48026a85128
@SQ	SN:2	LN:243199373	AS:NCBI37	UR:file:/data/local/ref/GATK/human_g1k_v37.fasta	M5:a0d9851da00400dec1098a9255ac712e
@SQ	SN:3	LN:198022430	AS:NCBI37	UR:file:/data/local/ref/GATK/human_g1k_v37.fasta	M5:fdfd811849cc2fadebc929bb925902e5
@RG	ID:UM0098:1	PL:ILLUMINA	PU:HWUSI-EAS1707-615LHAAXX-L001	LB:80	DT:2010-05-05T20:00:00-0400	SM:SD37743	CN:UMCORE
@RG	ID:UM0098:2	PL:ILLUMINA	PU:HWUSI-EAS1707-615LHAAXX-L002	LB:80	DT:2010-05-05T20:00:00-0400	SM:SD37743	CN:UMCORE
@PG	ID:bwa	VN:0.5.4
@PG	ID:GATK TableRecalibration	VN:1.0.3471	CL:Covariates=[ReadGroupCovariate, QualityScoreCovariate, CycleCovariate, DinucCovariate, TileCovariate], default_read_group=null, default_platform=null, force_read_group=null, force_platform=null, solid_recal_mode=SET_Q_ZERO, window_size_nqs=5, homopolymer_nback=7, exception_if_no_tile=false, ignore_nocall_colorspace=false, pQ=5, maxQ=40, smoothing=1
".Replace("  ", "\t");

            var ms = new MemoryStream(Encoding.UTF8.GetBytes(file));
            await using var _ = ms.ConfigureAwait(false);
            var reader = new SamReader(NullLogger.Instance);
            var definition = await reader.Read(ms).ConfigureAwait(false);

            Assert.Equal(3, definition.Sq.Length);
        }
    }
}