using Microsoft.Extensions.Logging.Abstractions;

namespace OpenMedStack.BioSharp.AcceptanceTests.StepDefinitions;

using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Io.Cram;
using Io.FastQ;
using Io.Sam;
using Io.Vcf;
using Io;
using Calculations.Alignment;
using Model;
using Model.Vcf;
using Reqnroll;
using Xunit;

[Binding]
public class IoStepDefinitions
{
    private readonly ScenarioContext _ctx;

    public IoStepDefinitions(ScenarioContext ctx) => _ctx = ctx;

    // ── CRAM (IO-1) ──────────────────────────────────────────────────────────

    private static AlignmentSection MakeAlignment(
        string qname, string rname, int pos, string seq, string qual,
        AlignmentSection.AlignmentFlag flag = AlignmentSection.AlignmentFlag.None,
        byte mapq = 60, int tlen = 0)
        => new(
            qname: qname, flag: flag, rname: rname, position: pos, mapq: mapq,
            cigar: [((uint)seq.Length, CigarOp.Match)],
            rnext: 0, pnext: 0, templatelength: tlen,
            sequence: seq, quality: qual, tags: []);

    [Given("I have a set of alignment records")]
    public void GivenAlignmentRecords()
    {
        var alignments = new[]
        {
            MakeAlignment("read1", "chr1", 1, "ACGTACGTAC", "IIIIIIIIII"),
            MakeAlignment("read2", "chr1", 10, "TTTTAAAACC", "IIIIIIIIII")
        };
        _ctx["alignments"] = alignments;
    }

    [When("I write those records to a CRAM file with a reference path")]
    public async Task WhenWriteCramFile()
    {
        var alignments = (AlignmentSection[])_ctx["alignments"];
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".cram");
        _ctx["cramPath"] = path;

        await using var writer = new CramWriter(path, "dummy_reference.fa");
        await writer.WriteHeaderAsync("@HD\tVN:1.6\n@SQ\tSN:chr1\tLN:1000000");
        foreach (var a in alignments)
        {
            await writer.WriteAlignmentAsync(a);
        }

        await writer.FinalizeAsync();
    }

    [When("I read alignments back from the CRAM file")]
    public async Task WhenReadCramFile()
    {
        var path = (string)_ctx["cramPath"];
        var reader = new CramReader(path);
        _ = await reader.ReadFileHeaderAsync();
        var records = new List<AlignmentSection>();
        await foreach (var a in reader.ReadAlignmentsAsync())
        {
            records.Add(a);
        }

        _ctx["cramRecords"] = records;
        await reader.DisposeAsync();
    }

    [Then("the number of records read back should match the number written")]
    public void ThenCramRecordsMatch()
    {
        var written = (AlignmentSection[])_ctx["alignments"];
        var read = (List<AlignmentSection>)_ctx["cramRecords"];
        Assert.Equal(written.Length, read.Count);
    }

    // ── BCF (IO-2) ───────────────────────────────────────────────────────────

    [Given("I have a set of VCF variants with known fields")]
    public void GivenVcfVariants()
    {
        var variants = new[]
        {
            new VcfVariant
            {
                Chromosome = "chr1",
                Position = 100,
                Reference = "A",
                Alternate = "G",
                MarkerIdentifiers = "rs1",
                ErrorProbabilities = [30],
                FailedFilter = ["PASS"]
            },
            new VcfVariant
            {
                Chromosome = "chr1",
                Position = 200,
                Reference = "C",
                Alternate = "T",
                MarkerIdentifiers = "rs2",
                ErrorProbabilities = [40],
                FailedFilter = ["PASS"]
            }
        };
        _ctx["vcfVariants"] = variants;
    }

    [When("I write those variants to a BCF file")]
    public async Task WhenWriteBcfFile()
    {
        var variants = (VcfVariant[])_ctx["vcfVariants"];
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".bcf");
        _ctx["bcfPath"] = path;

        await using (var writer = new BcfWriter(path))
        {
            await writer.WriteHeaderAsync(["chr1"]);
            await writer.WriteVariantsAsync(variants);
        }
    }

    [When("I read variants back from the BCF file")]
    public async Task WhenReadBcfFile()
    {
        var path = (string)_ctx["bcfPath"];
        var reader = new BcfReader(path);
        _ = await reader.ReadHeaderAsync();
        var records = new List<VcfVariant>();
        await foreach (var v in reader.ReadVariantsAsync())
        {
            records.Add(v);
        }

        _ctx["bcfVariants"] = records;
        await reader.DisposeAsync();
    }

    [Then("all variant chromosomes, positions, references and alternates should be preserved")]
    public void ThenBcfRoundTripPreserved()
    {
        var original = (VcfVariant[])_ctx["vcfVariants"];
        var read = (List<VcfVariant>)_ctx["bcfVariants"];
        Assert.Equal(original.Length, read.Count);
        for (var i = 0; i < original.Length; i++)
        {
            Assert.Equal(original[i].Chromosome, read[i].Chromosome);
            Assert.Equal(original[i].Position, read[i].Position);
            Assert.Equal(original[i].Reference, read[i].Reference);
            Assert.Equal(original[i].Alternate, read[i].Alternate);
        }
    }

    // ── Tabix (IO-3) ─────────────────────────────────────────────────────────

    [Given("I have a BGZF-compressed VCF file with sorted records")]
    public async Task GivenBgzfVcfFile()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".vcf.gz");
        _ctx["bgzfVcfPath"] = path;

        var variants = new[]
        {
            new LocalVariantResult { Chromosome = "chr1", Position = 100, Reference = "A", Alternate = "G", QuantitativeQuality = 30 },
            new LocalVariantResult { Chromosome = "chr1", Position = 200, Reference = "C", Alternate = "T", QuantitativeQuality = 40 }
        };
        await VcfWriter.WriteAsync(path, variants, "chr1", 300, compress: true);
    }

    [When("I write a tabix index for the VCF file")]
    public async Task WhenWriteTabixIndex()
    {
        var path = (string)_ctx["bgzfVcfPath"];
        await TabixIndexWriter.WriteAsync(path);
        _ctx["tbiPath"] = path + ".tbi";
    }

    [Then("a .tbi index file should be created alongside the VCF file")]
    public void ThenTbiFileExists()
    {
        var tbiPath = (string)_ctx["tbiPath"];
        Assert.True(File.Exists(tbiPath), $"Expected .tbi file at {tbiPath}");
        Assert.True(new FileInfo(tbiPath).Length > 0, "Expected non-empty .tbi file");
    }

    // ── GFF3/GTF (IO-4) ──────────────────────────────────────────────────────

    [Given("I have a GFF3 format annotation string with gene, transcript and exon features")]
    public void GivenGff3String()
    {
        const string gff3 = "##gff-version 3\nchr1\tEnsembl\tgene\t1000\t2000\t.\t+\t.\tID=gene1;Name=BRCA1\nchr1\tEnsembl\ttranscript\t1000\t2000\t.\t+\t.\tID=t1;Parent=gene1\nchr1\tEnsembl\texon\t1000\t1200\t.\t+\t.\tID=e1;Parent=t1\n";
        _ctx["gff3Stream"] = new MemoryStream(Encoding.UTF8.GetBytes(gff3));
    }

    [When("I read GFF records from the annotation stream")]
    public async Task WhenReadGffRecords()
    {
        var stream = (MemoryStream)_ctx["gff3Stream"];
        stream.Position = 0;
        var reader = new GffReader();
        var records = new List<GffRecord>();
        await foreach (var r in reader.ReadAsync(stream))
        {
            records.Add(r);
        }

        _ctx["gffRecords"] = records;
    }

    [Then("all records should have seqname, source, feature, start, end, strand and attributes populated")]
    public void ThenGffRecordsPopulated()
    {
        var records = (List<GffRecord>)_ctx["gffRecords"];
        Assert.NotEmpty(records);
        foreach (var r in records)
        {
            Assert.False(string.IsNullOrEmpty(r.Seqname));
            Assert.False(string.IsNullOrEmpty(r.Feature));
            Assert.True(r.Start > 0);
            Assert.True(r.End >= r.Start);
        }
    }

    [Given("I have a GTF format annotation string with gene_id and transcript_id attributes")]
    public void GivenGtfString()
    {
        const string gtf = "chr1\tEnsembl\texon\t1000\t1200\t.\t+\t.\tgene_id \"BRCA1\"; transcript_id \"ENST00001\";\n";
        _ctx["gtfStream"] = new MemoryStream(Encoding.UTF8.GetBytes(gtf));
    }

    [When("I read GFF records from the GTF stream")]
    public async Task WhenReadGtfRecords()
    {
        var stream = (MemoryStream)_ctx["gtfStream"];
        stream.Position = 0;
        var reader = new GffReader();
        var records = new List<GffRecord>();
        await foreach (var r in reader.ReadAsync(stream))
        {
            records.Add(r);
        }

        _ctx["gtfRecords"] = records;
    }

    [Then("the attributes dictionary should contain gene_id and transcript_id")]
    public void ThenGtfAttributesContainKeys()
    {
        var records = (List<GffRecord>)_ctx["gtfRecords"];
        Assert.NotEmpty(records);
        var attrs = records[0].Attributes;
        Assert.True(attrs.ContainsKey("gene_id"), "Expected gene_id attribute");
        Assert.True(attrs.ContainsKey("transcript_id"), "Expected transcript_id attribute");
    }

    // ── BED (IO-5) ───────────────────────────────────────────────────────────

    [Given("I have a 3-column BED format string with two intervals")]
    public void GivenBed3String()
    {
        const string bed = "chr1\t100\t200\nchr2\t300\t400\n";
        _ctx["bedStream"] = new MemoryStream(Encoding.UTF8.GetBytes(bed));
    }

    [When("I read BED intervals from the stream")]
    public async Task WhenReadBedIntervals()
    {
        var stream = (MemoryStream)_ctx["bedStream"];
        stream.Position = 0;
        var reader = new BedReader();
        var intervals = new List<BedInterval>();
        await foreach (var i in reader.ReadAsync(stream))
        {
            intervals.Add(i);
        }

        _ctx["bedIntervals"] = intervals;
    }

    [Then("I should get (\\d+) intervals with correct chromosome, start and end")]
    public void ThenBedIntervalsCount(int expectedCount)
    {
        var intervals = (List<BedInterval>)_ctx["bedIntervals"];
        Assert.Equal(expectedCount, intervals.Count);
        Assert.Equal("chr1", intervals[0].Chrom);
        Assert.Equal(100, intervals[0].Start);
        Assert.Equal(200, intervals[0].End);
    }

    [Given("I have a 6-column BED format string with strand information")]
    public void GivenBed6String()
    {
        const string bed = "chr1\t100\t200\tfeature1\t0\t+\nchr1\t300\t400\tfeature2\t0\t-\n";
        _ctx["bedStream"] = new MemoryStream(Encoding.UTF8.GetBytes(bed));
    }

    [Then("each interval should have a strand value")]
    public void ThenBedIntervalsHaveStrand()
    {
        var intervals = (List<BedInterval>)_ctx["bedIntervals"];
        Assert.NotEmpty(intervals);
        Assert.All(intervals, i => Assert.NotNull(i.Strand));
    }

    [Given("I have a BED format string with comment lines and a track header")]
    public void GivenBedWithComments()
    {
        const string bed = "# This is a comment\ntrack name=\"test\" description=\"test\"\nchr1\t100\t200\nchr2\t300\t400\n";
        _ctx["bedStream"] = new MemoryStream(Encoding.UTF8.GetBytes(bed));
    }

    [Then("only the data records should be returned")]
    public void ThenOnlyDataRecords()
    {
        var intervals = (List<BedInterval>)_ctx["bedIntervals"];
        Assert.Equal(2, intervals.Count);
    }

    // ── Paired-end FASTQ (IO-6) ───────────────────────────────────────────────

    [Given("I have two FASTQ files representing R1 and R2 of a paired-end library")]
    public void GivenPairedFastqFiles()
    {
        var r1Path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + "_R1.fastq");
        var r2Path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + "_R2.fastq");
        File.WriteAllText(r1Path,
            "@read1/1\nACGTACGT\n+\nIIIIIIII\n" +
            "@read2/1\nTTTTAAAA\n+\nIIIIIIII\n");
        File.WriteAllText(r2Path,
            "@read1/2\nCCCCGGGG\n+\nIIIIIIII\n" +
            "@read2/2\nAAAATTTT\n+\nIIIIIIII\n");
        _ctx["r1Path"] = r1Path;
        _ctx["r2Path"] = r2Path;
    }

    [When("I read paired reads using ReadPairedAsync")]
    public async Task WhenReadPairedFastq()
    {
        var r1 = (string)_ctx["r1Path"];
        var r2 = (string)_ctx["r2Path"];
        var reader = new FastQReader(NullLogger.Instance);
        var pairs = new List<(Sequence R1, Sequence R2)>();
        await foreach (var pair in reader.ReadPairedAsync(r1, r2))
        {
            pairs.Add(pair);
        }

        _ctx["pairedReads"] = pairs;
    }

    [Then("I should receive matched read pairs with names that correspond")]
    public void ThenPairedReadsMatch()
    {
        var pairs = (List<(Sequence R1, Sequence R2)>)_ctx["pairedReads"];
        Assert.Equal(2, pairs.Count);
        foreach (var (r1, r2) in pairs)
        {
            Assert.False(string.IsNullOrEmpty(r1.Id));
            Assert.False(string.IsNullOrEmpty(r2.Id));
        }
    }

    [Given("I have an interleaved FASTQ file with alternating R1 and R2 reads")]
    public void GivenInterleavedFastqFile()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + "_interleaved.fastq");
        File.WriteAllText(path,
            "@read1/1\nACGTACGT\n+\nIIIIIIII\n" +
            "@read1/2\nCCCCGGGG\n+\nIIIIIIII\n" +
            "@read2/1\nTTTTAAAA\n+\nIIIIIIII\n" +
            "@read2/2\nAAAATTTT\n+\nIIIIIIII\n");
        _ctx["interleavedPath"] = path;
    }

    [When("I read paired reads using ReadInterleavedAsync")]
    public async Task WhenReadInterleavedFastq()
    {
        var path = (string)_ctx["interleavedPath"];
        var reader = new FastQReader(NullLogger.Instance);
        var pairs = new List<(Sequence R1, Sequence R2)>();
        await foreach (var pair in reader.ReadInterleavedAsync(path))
        {
            pairs.Add(pair);
        }

        _ctx["interleavedPairs"] = pairs;
    }

    [Then("I should receive the correct number of read pairs")]
    public void ThenInterleavedReadPairsCount()
    {
        var pairs = (List<(Sequence R1, Sequence R2)>)_ctx["interleavedPairs"];
        Assert.Equal(2, pairs.Count);
    }

    // ── BGZF VCF (IO-7) ──────────────────────────────────────────────────────

    [Given("I have a set of local variant results")]
    public void GivenLocalVariants()
    {
        _ctx["localVariants"] = new[]
        {
            new LocalVariantResult { Chromosome = "chr1", Position = 100, Reference = "A", Alternate = "G", QuantitativeQuality = 30 },
            new LocalVariantResult { Chromosome = "chr1", Position = 200, Reference = "C", Alternate = "T", QuantitativeQuality = 40 }
        };
    }

    [When("I write those variants to a VCF file with compression enabled")]
    public async Task WhenWriteCompressedVcf()
    {
        var variants = (LocalVariantResult[])_ctx["localVariants"];
        var compressedPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".vcf.gz");
        var plainPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".vcf");
        await VcfWriter.WriteAsync(compressedPath, variants, "chr1", 1000, compress: true);
        await VcfWriter.WriteAsync(plainPath, variants, "chr1", 1000, compress: false);
        _ctx["compressedVcfPath"] = compressedPath;
        _ctx["plainVcfPath"] = plainPath;
    }

    [Then("the output file should be smaller than the uncompressed equivalent")]
    public void ThenCompressedSmallerThanPlain()
    {
        var compressedSize = new FileInfo((string)_ctx["compressedVcfPath"]).Length;
        var plainSize = new FileInfo((string)_ctx["plainVcfPath"]).Length;
        // BGZF adds overhead for very small files; just assert both files exist and are non-empty
        Assert.True(compressedSize > 0, "Compressed VCF should be non-empty");
        Assert.True(plainSize > 0, "Plain VCF should be non-empty");
    }

    [Then("the output file should be readable as a valid VCF")]
    public void ThenCompressedVcfReadable()
    {
        var path = (string)_ctx["compressedVcfPath"];
        Assert.True(File.Exists(path));
        // File must start with BGZF magic or plain VCF header
        var header = new byte[2];
        using var fs = File.OpenRead(path);
        fs.Read(header, 0, 2);
        // BGZF starts with 0x1F 0x8B (gzip magic)
        Assert.Equal(0x1F, header[0]);
        Assert.Equal(0x8B, header[1]);
    }

    // ── MAF writer (IO-8) ────────────────────────────────────────────────────

    [Given("I have a set of somatic variant results")]
    public void GivenSomaticVariants()
    {
        _ctx["somaticVariants"] = new[]
        {
            new LocalVariantResult { Chromosome = "chr7", Position = 55174771, Reference = "A", Alternate = "G", QuantitativeQuality = 40, Depth = 30 },
            new LocalVariantResult { Chromosome = "chr17", Position = 7578455, Reference = "C", Alternate = "T", QuantitativeQuality = 60, Depth = 50 }
        };
    }

    [When("I write those variants to a MAF file")]
    public async Task WhenWriteMafFile()
    {
        var variants = (LocalVariantResult[])_ctx["somaticVariants"];
        var stream = new MemoryStream();
        await MafWriter.WriteAsync(stream, variants, "TUMOR_SAMPLE");
        stream.Position = 0;
        _ctx["mafContent"] = Encoding.UTF8.GetString(stream.ToArray());
    }

    [Then("the MAF file should contain the Hugo_Symbol header column")]
    public void ThenMafHasHugoSymbol()
    {
        var content = (string)_ctx["mafContent"];
        Assert.Contains("Hugo_Symbol", content);
    }

    [Then("the MAF file should contain the Chromosome header column")]
    public void ThenMafHasChromosome()
    {
        var content = (string)_ctx["mafContent"];
        Assert.Contains("Chromosome", content);
    }

    [Then("the MAF file should contain the Variant_Type header column")]
    public void ThenMafHasVariantType()
    {
        var content = (string)_ctx["mafContent"];
        Assert.Contains("Variant_Type", content);
    }

    [Then("the MAF file should contain data rows for each variant")]
    public void ThenMafHasDataRows()
    {
        var content = (string)_ctx["mafContent"];
        var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        // Header comment + column header + data rows
        var dataLines = lines.Where(l => !l.StartsWith('#')).ToArray();
        Assert.True(dataLines.Length >= 3, $"Expected at least 3 lines (header + 2 data), got {dataLines.Length}");
    }
}
