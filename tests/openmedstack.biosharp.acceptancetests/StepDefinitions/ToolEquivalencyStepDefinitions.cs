namespace OpenMedStack.BioSharp.AcceptanceTests.StepDefinitions;

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Calculations;
using Calculations.Alignment;
using Calculations.BurrowsWheeler;
using Io.Bcl;
using Io.FastQ;
using Microsoft.Extensions.Logging.Abstractions;
using Model;
using Model.Bcl;using Reqnroll;
using Xunit;

/// <summary>
/// Step definitions for the <c>ToolEquivalency.feature</c> scenarios.
///
/// Each step that drives an external tool (bwa, fastp, fastqc, etc.) will have already
/// been guarded by the <see cref="ToolEquivalencyHooks"/> before-scenario skip check,
/// so the code here can assume the tool is available.
///
/// Context keys used throughout this class:
/// <list type="bullet">
///   <item><term>ScenarioTempDir</term><description>Temp directory for this scenario's files.</description></item>
///   <item><term>Reference</term><description><see cref="Sequence"/> (in-memory reference).</description></item>
///   <item><term>ReferenceFile</term><description>Path to the FASTA reference file.</description></item>
///   <item><term>Reads</term><description><see cref="Sequence"/>[] (in-memory reads).</description></item>
///   <item><term>ReadsFile</term><description>Path to the FASTQ reads file.</description></item>
///   <item><term>BamFile</term><description>Path to the sorted BAM file.</description></item>
///   <item><term>SnpPositions</term><description>Planted SNP 1-based positions (HashSet&lt;int&gt;).</description></item>
///   <item><term>BioSharpMappedCount</term><description>Reads mapped by BioSharp.</description></item>
///   <item><term>BioSharpSurvivingCount</term><description>Reads surviving BioSharp trimming.</description></item>
///   <item><term>BioSharpBasesRemoved</term><description>Bases removed by BioSharp trimmer.</description></item>
///   <item><term>BioSharpReport</term><description><see cref="FastQReport"/> from BioSharp.</description></item>
///   <item><term>BioSharpVariantPositions</term><description>SNP positions called by BioSharp.</description></item>
///   <item><term>ExternalMappedCount</term><description>Reads mapped by the external aligner.</description></item>
///   <item><term>ExternalAlignerName</term><description>Name of the external aligner used.</description></item>
///   <item><term>ExternalSurvivingCount</term><description>Reads surviving the external trimmer.</description></item>
///   <item><term>ExternalTrimmerName</term><description>Name of the external trimmer used.</description></item>
///   <item><term>ExternalBasesRemoved</term><description>Bases removed by the external trimmer.</description></item>
///   <item><term>FastqcTotalReads</term><description>Total reads reported by FastQC.</description></item>
///   <item><term>FastqcMeanQuality</term><description>Mean per-base quality reported by FastQC.</description></item>
///   <item><term>ExternalVariantPositions</term><description>SNP positions called by the external caller.</description></item>
///   <item><term>ExternalCallerName</term><description>Name of the external variant caller used.</description></item>
///   <item><term>ScenarioParams</term><description>Human-readable parameter string for reports.</description></item>
/// </list>
/// </summary>
[Binding]
public sealed class ToolEquivalencyStepDefinitions
{
    private const string Bases = "ACGT";
    private const int Threads = 4;
    private readonly ScenarioContext _ctx;

    public ToolEquivalencyStepDefinitions(ScenarioContext ctx) => _ctx = ctx;

    // ── Helpers ──────────────────────────────────────────────────────────────

    private string TempDir
    {
        get
        {
            if (!_ctx.TryGetValue("ScenarioTempDir", out var v) || v is not string dir)
            {
                dir = Path.Combine(Path.GetTempPath(), $"biosharp-equiv-{Guid.NewGuid():N}");
                Directory.CreateDirectory(dir);
                _ctx["ScenarioTempDir"] = dir;
            }

            return dir;
        }
    }

    private static string TempPath(string tempDir, string fileName) => Path.Combine(tempDir, fileName);

    private static void WriteFasta(string path, string id, string sequence)
    {
        using var w = new StreamWriter(path);
        w.WriteLine($">{id}");
        for (var i = 0; i < sequence.Length; i += 70)
        {
            w.WriteLine(sequence.Substring(i, Math.Min(70, sequence.Length - i)));
        }
    }

    private static void WriteFastq(string path, IEnumerable<Sequence> reads)
    {
        using var w = new StreamWriter(path);
        foreach (var r in reads)
        {
            w.WriteLine($"@{r.Id}");
            w.WriteLine(r.GetData().ToString());
            w.WriteLine("+");
            w.WriteLine(r.GetQuality().ToString());
        }
    }

    private static string BuildSyntheticReference(int size, int seed)
    {
        var rng = new Random(seed);
        var buf = new char[size];
        for (var i = 0; i < size; i++)
        {
            buf[i] = Bases[rng.Next(4)];
        }

        return new string(buf);
    }

    private static (string RefSeq, HashSet<int> SnpPositions) BuildReferenceWithSnps(
        int size, int snpCount, int seed)
    {
        var refStr = BuildSyntheticReference(size, seed);
        var buf = refStr.ToCharArray();
        var rng = new Random(seed + 1);
        var positions = new HashSet<int>();
        var attempts = 0;

        while (positions.Count < snpCount && attempts < snpCount * 10)
        {
            attempts++;
            // Keep SNPs away from the very start/end so reads spanning them align cleanly
            var pos = rng.Next(200, size - 200);
            if (positions.Contains(pos))
            {
                continue;
            }

            var original = buf[pos];
            char alt;
            do
            {
                alt = Bases[rng.Next(4)];
            } while (alt == original);

            buf[pos] = alt;
            positions.Add(pos + 1); // 1-based
        }

        return (new string(buf), positions);
    }

    private static Sequence[] BuildReadsFromReference(
        string refStr, int readCount, int readLength, int snpRatePct, int seed)
    {
        var rng = new Random(seed);
        var reads = new Sequence[readCount];
        for (var i = 0; i < readCount; i++)
        {
            var start = rng.Next(0, Math.Max(1, refStr.Length - readLength));
            var buf = refStr.Substring(start, Math.Min(readLength, refStr.Length - start)).ToCharArray();
            if (buf.Length < readLength)
            {
                Array.Resize(ref buf, readLength);
                for (var j = refStr.Length - start; j < readLength; j++)
                {
                    buf[j] = Bases[rng.Next(4)];
                }
            }

            // Inject SNPs at the given rate
            if (snpRatePct > 0 && rng.Next(100) < snpRatePct)
            {
                var snpPos = rng.Next(buf.Length);
                var orig = buf[snpPos];
                char snp;
                do
                {
                    snp = Bases[rng.Next(4)];
                } while (snp == orig);

                buf[snpPos] = snp;
            }

            reads[i] = new Sequence($"r{i}", new string(buf).AsMemory(),
                new string('I', buf.Length).AsMemory());
        }

        return reads;
    }

    private static Sequence[] BuildReadsOverSnpPositions(
        string refStr, int readCount, int readLength, IReadOnlyCollection<int> snpPositions, int seed)
    {
        // First half of reads are derived from positions covering SNPs; the rest are random
        var rng = new Random(seed);
        var reads = new List<Sequence>(readCount);
        var snpList = snpPositions.ToArray();

        // Reads covering SNP positions (with the SNP already in the reference at that position)
        foreach (var snpPos1Based in snpList)
        {
            var pos0 = snpPos1Based - 1;
            var start = Math.Max(0, pos0 - readLength / 2);
            var end = Math.Min(refStr.Length, start + readLength);
            var actualLen = end - start;
            var buf = refStr.Substring(start, actualLen).ToCharArray();
            if (actualLen < readLength)
            {
                Array.Resize(ref buf, readLength);
                for (var j = actualLen; j < readLength; j++)
                {
                    buf[j] = Bases[rng.Next(4)];
                }
            }

            reads.Add(new Sequence($"snp_r{reads.Count}", new string(buf).AsMemory(),
                new string('I', readLength).AsMemory()));
        }

        // Fill remaining with random reads derived from reference
        while (reads.Count < readCount)
        {
            var start = rng.Next(0, Math.Max(1, refStr.Length - readLength));
            var actualLen = Math.Min(readLength, refStr.Length - start);
            var buf = refStr.Substring(start, actualLen).ToCharArray();
            if (actualLen < readLength)
            {
                Array.Resize(ref buf, readLength);
                for (var j = actualLen; j < readLength; j++)
                {
                    buf[j] = Bases[rng.Next(4)];
                }
            }

            reads.Add(new Sequence($"r{reads.Count}", new string(buf).AsMemory(),
                new string('I', readLength).AsMemory()));
        }

        return reads.ToArray();
    }

    private static int CountMappedSamRecords(string samPath)
    {
        return File.ReadLines(samPath)
            .Where(l => !l.StartsWith('@'))
            .Count(l =>
            {
                var fields = l.Split('\t');
                return fields.Length >= 2
                    && int.TryParse(fields[1], out var flag)
                    && (flag & 4) == 0;     // bit 4 = unmapped
            });
    }

    private static HashSet<int> ParseVcfPositions(string vcfPath)
    {
        return File.ReadLines(vcfPath)
            .Where(l => !l.StartsWith('#') && !string.IsNullOrWhiteSpace(l))
            .Select(l =>
            {
                var fields = l.Split('\t');
                return fields.Length >= 2 && int.TryParse(fields[1], out var pos) ? pos : -1;
            })
            .Where(p => p > 0)
            .ToHashSet();
    }

    // Computes Jaccard overlap percentage: |A ∩ B| / |A ∪ B| * 100
    private static double JaccardOverlapPct(HashSet<int> a, HashSet<int> b)
    {
        if (a.Count == 0 && b.Count == 0)
        {
            return 100.0;
        }

        var intersection = a.Intersect(b).Count();
        var union = a.Union(b).Count();
        return union == 0 ? 0.0 : (double)intersection / union * 100.0;
    }

    private static void AssertWithinTolerance(
        double biosharpValue, double externalValue, double tolerancePct,
        string category, string externalTool, string parameters, string metric)
    {
        var delta = externalValue == 0
            ? (biosharpValue == 0 ? 0.0 : 100.0)
            : Math.Abs(biosharpValue - externalValue) / externalValue * 100.0;

        var passed = delta <= tolerancePct;

        EquivalencyResultCollector.Add(new EquivalencyResult(
            category, externalTool, parameters, metric,
            biosharpValue, externalValue, tolerancePct, passed));

        Assert.True(passed,
            $"[{category}] BioSharp {metric} ({biosharpValue:F1}) deviated from {externalTool} " +
            $"({externalValue:F1}) by {delta:F1}%, exceeding the {tolerancePct}% tolerance. " +
            $"Parameters: {parameters}");
    }

    // ── Given: Reference and read generation ─────────────────────────────────

    [Given("a synthetic FASTA reference of {int} bases seeded with random 42")]
    public void GivenSyntheticReference(int referenceSize)
    {
        var tempDir = TempDir;
        var refStr = BuildSyntheticReference(referenceSize, seed: 42);
        var refSeq = new Sequence("chrSynth", refStr.AsMemory(), new string('I', refStr.Length).AsMemory());
        var refFile = TempPath(tempDir, "ref.fa");
        WriteFasta(refFile, "chrSynth", refStr);

        _ctx["Reference"] = refSeq;
        _ctx["ReferenceFile"] = refFile;
        _ctx["ScenarioParams"] = $"refSize={referenceSize}";
    }

    [Given("a synthetic FASTA reference of {int} bases with {int} planted SNPs seeded with random 42")]
    public void GivenSyntheticReferenceWithSnps(int referenceSize, int snpCount)
    {
        var tempDir = TempDir;
        var (refStr, snpPositions) = BuildReferenceWithSnps(referenceSize, snpCount, seed: 42);
        var refSeq = new Sequence("chrSynth", refStr.AsMemory(), new string('I', refStr.Length).AsMemory());
        var refFile = TempPath(tempDir, "ref.fa");
        WriteFasta(refFile, "chrSynth", refStr);

        _ctx["Reference"] = refSeq;
        _ctx["ReferenceFile"] = refFile;
        _ctx["SnpPositions"] = snpPositions;
        _ctx["ScenarioParams"] = $"refSize={referenceSize},snps={snpCount}";
    }

    [Given("{int} synthetic FASTQ reads of {int} bp derived from the reference with {int} percent SNP rate")]
    public void GivenSyntheticReadsWithSnpRate(int readCount, int readLength, int snpRatePct)
    {
        var tempDir = TempDir;
        var refSeq = (Sequence)_ctx["Reference"];
        var refStr = refSeq.GetData().ToString()!;
        var reads = BuildReadsFromReference(refStr, readCount, readLength, snpRatePct, seed: 42);
        var readsFile = TempPath(tempDir, "reads.fq");
        WriteFastq(readsFile, reads);

        _ctx["Reads"] = reads;
        _ctx["ReadsFile"] = readsFile;

        var existing = _ctx.TryGetValue("ScenarioParams", out var p) ? (string)p! : "";
        _ctx["ScenarioParams"] = $"{existing},reads={readCount},len={readLength},snpRate={snpRatePct}%";
    }

    [Given("{int} FASTQ reads of {int} bp covering those SNP positions")]
    public void GivenReadsCoveringSnpPositions(int readCount, int readLength)
    {
        var tempDir = TempDir;
        var refSeq = (Sequence)_ctx["Reference"];
        var refStr = refSeq.GetData().ToString()!;
        var snpPositions = (HashSet<int>)_ctx["SnpPositions"];
        var reads = BuildReadsOverSnpPositions(refStr, readCount, readLength, snpPositions, seed: 42);
        var readsFile = TempPath(tempDir, "reads.fq");
        WriteFastq(readsFile, reads);

        _ctx["Reads"] = reads;
        _ctx["ReadsFile"] = readsFile;

        var existing = _ctx.TryGetValue("ScenarioParams", out var p) ? (string)p! : "";
        _ctx["ScenarioParams"] = $"{existing},reads={readCount},len={readLength}";
    }

    [Given("{int} FASTQ reads of {int} bp with {int} percent carrying adapter {string}")]
    public void GivenReadsWithAdapter(int readCount, int readLength, int adapterFraction, string adapter)
    {
        var tempDir = TempDir;
        var rng = new Random(42);
        var reads = new Sequence[readCount];
        var adapterChars = adapter.ToCharArray();

        for (var i = 0; i < readCount; i++)
        {
            var baseBases = new char[readLength];
            for (var j = 0; j < readLength; j++)
            {
                baseBases[j] = Bases[rng.Next(4)];
            }

            if (rng.Next(100) < adapterFraction)
            {
                // Insert adapter at a random position within the last 2/3 of the read
                var insertAt = rng.Next(readLength / 3, readLength);
                var remaining = readLength - insertAt;
                var adapterLen = Math.Min(adapterChars.Length, remaining);
                Array.Copy(adapterChars, 0, baseBases, insertAt, adapterLen);
            }

            reads[i] = new Sequence($"r{i}", new string(baseBases).AsMemory(),
                new string('I', readLength).AsMemory());
        }

        var readsFile = TempPath(tempDir, "reads.fq");
        WriteFastq(readsFile, reads);

        _ctx["Reads"] = reads;
        _ctx["ReadsFile"] = readsFile;
        _ctx["ScenarioParams"] = $"reads={readCount},len={readLength},adapterFrac={adapterFraction}%";
    }

    [Given("{int} uniform-quality FASTQ reads of {int} bp with quality score {int}")]
    public void GivenUniformQualityReads(int readCount, int readLength, int qualScore)
    {
        var tempDir = TempDir;
        var rng = new Random(42);
        var qualChar = (char)(qualScore + 33); // Phred+33 encoding
        var reads = new Sequence[readCount];

        for (var i = 0; i < readCount; i++)
        {
            var buf = new char[readLength];
            for (var j = 0; j < readLength; j++)
            {
                buf[j] = Bases[rng.Next(4)];
            }

            reads[i] = new Sequence($"r{i}", new string(buf).AsMemory(),
                new string(qualChar, readLength).AsMemory());
        }

        var readsFile = TempPath(tempDir, "reads.fq");
        WriteFastq(readsFile, reads);

        _ctx["Reads"] = reads;
        _ctx["ReadsFile"] = readsFile;
        _ctx["ScenarioParams"] = $"reads={readCount},len={readLength},qual={qualScore}";
    }

    // ── When: BioSharp alignment ──────────────────────────────────────────────

    [When("BioSharp aligns the reads using the HashMap seeder with seed size {int}")]
    public async Task WhenBioSharpAlignsHashMap(int seedSize)
    {
        var reference = (Sequence)_ctx["Reference"];
        var readsFile = (string)_ctx["ReadsFile"];

        var pipeline = new VariantCallingPipeline(reference, "chrSynth",
            new VariantCallingPipeline.PipelineOptions
            {
                SeedSize = seedSize,
                MinAlignmentScore = 10,
                EnableGraphSvDetection = false,
                EnableSoftClipRealignment = false,
                DegreeOfParallelism = Threads
            });

        await pipeline.LoadFastQ(readsFile).ConfigureAwait(false);
        var result = pipeline.BuildResult();
        _ctx["BioSharpMappedCount"] = result.Metrics.ReadsMapped;
    }

    [When("BioSharp aligns the reads using the FmIndex seeder")]
    public async Task WhenBioSharpAlignsFmIndex()
    {
        var reference = (Sequence)_ctx["Reference"];
        var readsFile = (string)_ctx["ReadsFile"];

        var pipeline = new VariantCallingPipeline(reference, "chrSynth",
            new VariantCallingPipeline.PipelineOptions
            {
                MinAlignmentScore = 10,
                EnableGraphSvDetection = false,
                EnableSoftClipRealignment = false,
                DegreeOfParallelism = Threads
            });

        pipeline.Seeder = new FmIndexSeeder(reference);
        await pipeline.LoadFastQ(readsFile).ConfigureAwait(false);
        var result = pipeline.BuildResult();
        _ctx["BioSharpMappedCount"] = result.Metrics.ReadsMapped;
    }

    // ── When: External alignment ──────────────────────────────────────────────

    [When("bwa mem aligns the same reads to the same reference")]
    public void WhenBwaMemAligns()
    {
        var tempDir = TempDir;
        var refFile = (string)_ctx["ReferenceFile"];
        var readsFile = (string)_ctx["ReadsFile"];

        // Build index once per scenario
        if (!_ctx.TryGetValue("BwaIndexBuilt", out var indexBuilt) || !(bool)indexBuilt!)
        {
            var indexExit = ExternalToolRunner.Run("bwa", $"index \"{refFile}\"", tempDir, 120_000);
            if (indexExit != 0)
            {
                throw new InvalidOperationException($"bwa index failed with exit code {indexExit}");
            }

            _ctx["BwaIndexBuilt"] = true;
        }

        var samFile = TempPath(tempDir, $"bwa_{Guid.NewGuid():N}.sam");
        var logFile = TempPath(tempDir, "bwa.log");
        var exit = ExternalToolRunner.Shell(
            $"bwa mem -t {Threads} \"{refFile}\" \"{readsFile}\" > \"{samFile}\" 2> \"{logFile}\"",
            tempDir, 180_000);

        if (exit != 0)
        {
            var log = File.Exists(logFile) ? File.ReadAllText(logFile) : "(no log)";
            throw new InvalidOperationException($"bwa mem failed (exit {exit}). Log: {log}");
        }

        _ctx["ExternalMappedCount"] = CountMappedSamRecords(samFile);
        _ctx["ExternalAlignerName"] = "bwa mem";

        if (File.Exists(samFile))
        {
            File.Delete(samFile);
        }

        if (File.Exists(logFile))
        {
            File.Delete(logFile);
        }
    }

    [When("bwa-mem2 aligns the same reads to the same reference")]
    public void WhenBwaMem2Aligns()
    {
        var tempDir = TempDir;
        var refFile = (string)_ctx["ReferenceFile"];
        var readsFile = (string)_ctx["ReadsFile"];

        if (!_ctx.TryGetValue("BwaMem2IndexBuilt", out var indexBuilt) || !(bool)indexBuilt!)
        {
            var indexExit = ExternalToolRunner.Run("bwa-mem2", $"index \"{refFile}\"", tempDir, 120_000);
            if (indexExit != 0)
            {
                throw new InvalidOperationException($"bwa-mem2 index failed with exit code {indexExit}");
            }

            _ctx["BwaMem2IndexBuilt"] = true;
        }

        var samFile = TempPath(tempDir, $"bwamem2_{Guid.NewGuid():N}.sam");
        var logFile = TempPath(tempDir, "bwa-mem2.log");
        var exit = ExternalToolRunner.Shell(
            $"bwa-mem2 mem -t {Threads} \"{refFile}\" \"{readsFile}\" > \"{samFile}\" 2> \"{logFile}\"",
            tempDir, 180_000);

        if (exit != 0)
        {
            var log = File.Exists(logFile) ? File.ReadAllText(logFile) : "(no log)";
            throw new InvalidOperationException($"bwa-mem2 mem failed (exit {exit}). Log: {log}");
        }

        _ctx["ExternalMappedCount"] = CountMappedSamRecords(samFile);
        _ctx["ExternalAlignerName"] = "bwa-mem2";

        if (File.Exists(samFile))
        {
            File.Delete(samFile);
        }

        if (File.Exists(logFile))
        {
            File.Delete(logFile);
        }
    }

    // ── When: Adapter trimming ────────────────────────────────────────────────

    [When("BioSharp trims adapter {string} with minimum length {int} and max mismatches {int}")]
    public void WhenBioSharpTrims(string adapter, int minLength, int maxMismatches)
    {
        var reads = (Sequence[])_ctx["Reads"];
        var trimmer = new AdapterTrimmer(adapter, maxMismatches: maxMismatches, minLength: minLength);
        var result = trimmer.TrimAll(reads);

        var survivingCount = result.Reads.Count(r => r != null);
        _ctx["BioSharpSurvivingCount"] = survivingCount;
        _ctx["BioSharpBasesRemoved"] = result.Stats.BasesRemoved;
    }

    [When("fastp trims the reads with adapter {string} and minimum length {int}")]
    public void WhenFastpTrims(string adapter, int minLength)
    {
        var tempDir = TempDir;
        var readsFile = (string)_ctx["ReadsFile"];
        var outFile = TempPath(tempDir, $"fastp_out_{Guid.NewGuid():N}.fq.gz");
        var jsonFile = TempPath(tempDir, $"fastp_{Guid.NewGuid():N}.json");
        var htmlFile = TempPath(tempDir, $"fastp_{Guid.NewGuid():N}.html");

        try
        {
            var exit = ExternalToolRunner.Shell(
                $"fastp -w {Threads} -i \"{readsFile}\" -o \"{outFile}\" " +
                $"--adapter_sequence \"{adapter}\" -l {minLength} " +
                $"-j \"{jsonFile}\" -h \"{htmlFile}\" > {ExternalToolRunner.NullDevice} 2>&1",
                tempDir, 180_000);

            if (exit != 0)
            {
                throw new InvalidOperationException($"fastp failed with exit code {exit}");
            }

            // Parse fastp JSON for summary statistics
            var json = File.ReadAllText(jsonFile);
            using var doc = JsonDocument.Parse(json);
            var summary = doc.RootElement.GetProperty("summary");
            var beforeTotal = summary.GetProperty("before_filtering").GetProperty("total_reads").GetInt64();
            var afterTotal = summary.GetProperty("after_filtering").GetProperty("total_reads").GetInt64();
            var beforeBases = summary.GetProperty("before_filtering").GetProperty("total_bases").GetInt64();
            var afterBases = summary.GetProperty("after_filtering").GetProperty("total_bases").GetInt64();

            _ctx["ExternalSurvivingCount"] = (int)afterTotal;
            _ctx["ExternalBasesRemoved"] = beforeBases - afterBases;
            _ctx["ExternalTrimmerName"] = "fastp";
        }
        finally
        {
            if (File.Exists(outFile))
            {
                File.Delete(outFile);
            }

            if (File.Exists(htmlFile))
            {
                File.Delete(htmlFile);
            }
        }
    }

    [When("cutadapt trims the reads with adapter {string} and minimum length {int}")]
    public void WhenCutadaptTrims(string adapter, int minLength)
    {
        var tempDir = TempDir;
        var readsFile = (string)_ctx["ReadsFile"];
        var outFile = TempPath(tempDir, $"cutadapt_out_{Guid.NewGuid():N}.fq");

        try
        {
            var (exit, stdout) = ExternalToolRunner.ShellCapture(
                $"cutadapt -j {Threads} -a \"{adapter}\" -m {minLength} -o \"{outFile}\" \"{readsFile}\" 2>&1",
                tempDir, 180_000);

            if (exit != 0)
            {
                throw new InvalidOperationException(
                    $"cutadapt failed with exit code {exit}. Output: {stdout[..Math.Min(500, stdout.Length)]}");
            }

            // Parse surviving read count and bases removed from stdout
            var survivingCount = ParseCutadaptPassingReads(stdout);
            var basesRemoved = ParseCutadaptBasesRemoved(stdout);

            _ctx["ExternalSurvivingCount"] = survivingCount;
            _ctx["ExternalBasesRemoved"] = basesRemoved;
            _ctx["ExternalTrimmerName"] = "cutadapt";
        }
        finally
        {
            if (File.Exists(outFile))
            {
                File.Delete(outFile);
            }
        }
    }

    [When("trimmomatic trims the reads with adapter {string} and minimum length {int}")]
    public void WhenTrimmomaticTrims(string adapter, int minLength)
    {
        var tempDir = TempDir;
        var readsFile = (string)_ctx["ReadsFile"];
        var adapterFa = TempPath(tempDir, $"trimmomatic_adapter_{Guid.NewGuid():N}.fa");
        var outFile = TempPath(tempDir, $"trimmomatic_out_{Guid.NewGuid():N}.fq");

        try
        {
            File.WriteAllText(adapterFa, $">adapter\n{adapter}\n");

            var inputBases = CountBasesInFastqGz(readsFile);

            var (exit, stderr) = ExternalToolRunner.ShellCapture(
                $"trimmomatic SE -threads {Threads} \"{readsFile}\" \"{outFile}\" ILLUMINACLIP:\"{adapterFa}\":2:30:10 MINLEN:{minLength} 2>&1",
                tempDir, 180_000);

            if (exit != 0)
            {
                throw new InvalidOperationException(
                    $"trimmomatic failed with exit code {exit}. Output: {stderr[..Math.Min(500, stderr.Length)]}");
            }

            var survivingCount = ParseTrimmomaticSurvivingCount(stderr);
            var outputBases = File.Exists(outFile) ? CountBasesInFastq(outFile) : 0L;
            var basesRemoved = inputBases > 0 && outputBases >= 0 ? inputBases - outputBases : 0L;

            _ctx["ExternalSurvivingCount"] = survivingCount;
            _ctx["ExternalBasesRemoved"] = basesRemoved;
            _ctx["ExternalTrimmerName"] = "trimmomatic";
        }
        finally
        {
            if (File.Exists(adapterFa))
            {
                File.Delete(adapterFa);
            }

            if (File.Exists(outFile))
            {
                File.Delete(outFile);
            }
        }
    }

    private static int ParseTrimmomaticSurvivingCount(string output)
    {
        // Trimmomatic reports: "Input Reads: 1000 Surviving: 970 (97.00%) Dropped: 30 (3.00%)"
        foreach (var line in output.Split('\n'))
        {
            var idx = line.IndexOf("Surviving:", StringComparison.OrdinalIgnoreCase);
            if (idx < 0)
            {
                continue;
            }

            var after = line[(idx + "Surviving:".Length)..].Trim();
            var token = after.Split(' ')[0];
            if (int.TryParse(token, out var count))
            {
                return count;
            }
        }

        return -1;
    }

    private static long CountBasesInFastqGz(string path)
    {
        if (!File.Exists(path))
        {
            return 0;
        }

        using var fs = File.OpenRead(path);
        using var gz = new GZipStream(fs, CompressionMode.Decompress);
        using var sr = new StreamReader(gz, Encoding.UTF8, leaveOpen: false);
        long bases = 0;
        var lineIndex = 0;
        string? line;
        while ((line = sr.ReadLine()) != null)
        {
            if (lineIndex % 4 == 1)
            {
                bases += line.Length;
            }

            lineIndex++;
        }

        return bases;
    }

    private static long CountBasesInFastq(string path)
    {
        if (!File.Exists(path))
        {
            return 0;
        }

        using var fs = File.OpenRead(path);
        using var sr = new StreamReader(fs, Encoding.UTF8, leaveOpen: false);
        long bases = 0;
        var lineIndex = 0;
        string? line;
        while ((line = sr.ReadLine()) != null)
        {
            if (lineIndex % 4 == 1)
            {
                bases += line.Length;
            }

            lineIndex++;
        }

        return bases;
    }

    private static int ParseCutadaptPassingReads(string output)
    {
        foreach (var line in output.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("Reads written (passing filters):", StringComparison.OrdinalIgnoreCase))
            {
                // "Reads written (passing filters):     970 (97.0%)"
                var parts = trimmed.Split(':');
                if (parts.Length >= 2)
                {
                    var valuePart = parts[1].Trim().Split(' ')[0].Replace(",", "");
                    if (int.TryParse(valuePart, out var count))
                    {
                        return count;
                    }
                }
            }
        }

        return -1;
    }

    private static long ParseCutadaptBasesRemoved(string output)
    {
        long totalBefore = 0, totalAfter = 0;
        foreach (var line in output.Split('\n'))
        {
            var t = line.Trim();
            if (t.StartsWith("Total basepairs processed:", StringComparison.OrdinalIgnoreCase))
            {
                totalBefore = ParseCutadaptBytes(t);
            }
            else if (t.StartsWith("Total written (filtered):", StringComparison.OrdinalIgnoreCase))
            {
                totalAfter = ParseCutadaptBytes(t);
            }
        }

        return totalBefore > 0 && totalAfter > 0 ? totalBefore - totalAfter : 0;
    }

    private static long ParseCutadaptBytes(string line)
    {
        var colon = line.IndexOf(':');
        if (colon < 0)
        {
            return 0;
        }

        var value = line[(colon + 1)..].Trim().Split(' ')[0].Replace(",", "");
        return long.TryParse(value, out var n) ? n : 0;
    }

    // ── When: QC metrics ──────────────────────────────────────────────────────

    [When("BioSharp computes the FastQ quality report")]
    public async Task WhenBioSharpComputesQcReport()
    {
        var reads = (Sequence[])_ctx["Reads"];

        var report = await FastQQualityReport.Compute(System.Linq.AsyncEnumerable.ToAsyncEnumerable(reads)).ConfigureAwait(false);
        _ctx["BioSharpReport"] = report;
    }

    [When("fastqc analyses the same FASTQ reads")]
    public void WhenFastqcAnalyses()
    {
        var tempDir = TempDir;
        var readsFile = (string)_ctx["ReadsFile"];
        var fastqcOutDir = TempPath(tempDir, "fastqc_output");
        Directory.CreateDirectory(fastqcOutDir);

        var exit = ExternalToolRunner.Shell(
            $"fastqc --quiet --outdir \"{fastqcOutDir}\" \"{readsFile}\" > {ExternalToolRunner.NullDevice} 2>&1",
            tempDir, 300_000);

        if (exit != 0)
        {
            throw new InvalidOperationException($"fastqc failed with exit code {exit}");
        }

        // FastQC creates: reads_fastqc.zip in fastqcOutDir
        var zipFile = Directory.EnumerateFiles(fastqcOutDir, "*_fastqc.zip").FirstOrDefault()
            ?? throw new InvalidOperationException("fastqc did not produce a .zip output file");

        var (totalReads, meanQuality) = ParseFastqcZip(zipFile);
        _ctx["FastqcTotalReads"] = totalReads;
        _ctx["FastqcMeanQuality"] = meanQuality;
    }

    private static (long TotalReads, double MeanQuality) ParseFastqcZip(string zipPath)
    {
        using var zip = ZipFile.OpenRead(zipPath);
        var dataEntry = zip.Entries.FirstOrDefault(e => e.Name == "fastqc_data.txt")
            ?? throw new InvalidOperationException("fastqc_data.txt not found inside the FastQC zip");

        using var stream = dataEntry.Open();
        using var reader = new StreamReader(stream);
        var content = reader.ReadToEnd();

        long totalReads = 0;
        var qualMeans = new List<double>();
        var inBasicStats = false;
        var inPerBaseQual = false;

        foreach (var line in content.Split('\n'))
        {
            var t = line.Trim();

            if (t.StartsWith(">>Basic Statistics", StringComparison.Ordinal))
            {
                inBasicStats = true;
                inPerBaseQual = false;
                continue;
            }

            if (t.StartsWith(">>Per base sequence quality", StringComparison.Ordinal))
            {
                inBasicStats = false;
                inPerBaseQual = true;
                continue;
            }

            if (t.StartsWith(">>END_MODULE", StringComparison.Ordinal))
            {
                inBasicStats = false;
                inPerBaseQual = false;
                continue;
            }

            if (t.StartsWith('#'))
            {
                continue;
            }

            if (inBasicStats && t.StartsWith("Total Sequences", StringComparison.OrdinalIgnoreCase))
            {
                var parts = t.Split('\t');
                if (parts.Length >= 2 && long.TryParse(parts[1].Trim(), out var n))
                {
                    totalReads = n;
                }
            }

            if (inPerBaseQual && !string.IsNullOrWhiteSpace(t))
            {
                // Line format: "1    30.0    30.0    30.0    30.0    30.0    30.0"
                var parts = t.Split('\t');
                if (parts.Length >= 2 && double.TryParse(parts[1], System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var mean))
                {
                    qualMeans.Add(mean);
                }
            }
        }

        var avgMean = qualMeans.Count > 0 ? qualMeans.Average() : 0.0;
        return (totalReads, avgMean);
    }

    // ── When: Variant calling (BAM production + callers) ─────────────────────

    [When("a sorted BAM is produced by aligning the reads with bwa mem and sorting with samtools")]
    public void WhenSortedBamIsProduced()
    {
        var tempDir = TempDir;
        var refFile = (string)_ctx["ReferenceFile"];
        var readsFile = (string)_ctx["ReadsFile"];

        // Build BWA index
        if (!_ctx.TryGetValue("BwaIndexBuilt", out var indexBuilt) || !(bool)indexBuilt!)
        {
            var indexExit = ExternalToolRunner.Run("bwa", $"index \"{refFile}\"", tempDir, 120_000);
            if (indexExit != 0)
            {
                throw new InvalidOperationException($"bwa index failed (exit {indexExit})");
            }

            _ctx["BwaIndexBuilt"] = true;
        }

        var tmpSam = TempPath(tempDir, "aln.sam");
        var sortedBam = TempPath(tempDir, "aln.sorted.bam");
        var logFile = TempPath(tempDir, "bwa_samtools.log");

        // Align then sort via pipeline
        var exit = ExternalToolRunner.Shell(
            $"bwa mem -t {Threads} \"{refFile}\" \"{readsFile}\" 2>> \"{logFile}\" | " +
            $"samtools sort -@ {Threads} -o \"{sortedBam}\" - 2>> \"{logFile}\"",
            tempDir, 300_000);

        if (exit != 0)
        {
            var log = File.Exists(logFile) ? File.ReadAllText(logFile) : "(no log)";
            throw new InvalidOperationException($"bwa mem | samtools sort failed (exit {exit}). Log: {log}");
        }

        // Index the BAM
        var indexBamExit = ExternalToolRunner.Run(
            "samtools", $"index \"{sortedBam}\"", tempDir, 60_000);
        if (indexBamExit != 0)
        {
            throw new InvalidOperationException($"samtools index failed (exit {indexBamExit})");
        }

        _ctx["BamFile"] = sortedBam;
        if (File.Exists(logFile))
        {
            File.Delete(logFile);
        }
    }

    [When("BioSharp calls variants from that BAM using the HashMap seeder")]
    public async Task WhenBioSharpCallsVariantsFromBam()
    {
        var reference = (Sequence)_ctx["Reference"];
        var bamFile = (string)_ctx["BamFile"];

        var pipeline = new VariantCallingPipeline(reference, "chrSynth",
            new VariantCallingPipeline.PipelineOptions
            {
                SeedSize = 11,
                MinAlignmentScore = 10,
                MinVariantQuality = 20,
                MinAlternateObservationCount = 2,
                MinAlternateFraction = 0.10,
                EnableGraphSvDetection = false,
                EnableSoftClipRealignment = false,
                DegreeOfParallelism = Threads,
                UseBamAlignmentCoordinates = false
            });

        await pipeline.LoadBam(bamFile).ConfigureAwait(false);
        var result = pipeline.BuildResult();

        var positions = result.Variants
            .Where(v => v.Reference.Length == 1 && v.Alternate.Length == 1) // SNPs only
            .Select(v => v.Position)
            .ToHashSet();

        _ctx["BioSharpVariantPositions"] = positions;
    }

    [When("freebayes calls variants from the same BAM")]
    public void WhenFreebayesCallsVariants()
    {
        var tempDir = TempDir;
        var refFile = (string)_ctx["ReferenceFile"];
        var bamFile = (string)_ctx["BamFile"];
        var vcfFile = TempPath(tempDir, $"freebayes_{Guid.NewGuid():N}.vcf");

        var exit = ExternalToolRunner.Shell(
            $"freebayes -f \"{refFile}\" \"{bamFile}\" > \"{vcfFile}\" 2> {ExternalToolRunner.NullDevice}",
            tempDir, 300_000);

        if (exit != 0)
        {
            throw new InvalidOperationException($"freebayes failed with exit code {exit}");
        }

        var positions = ParseVcfPositions(vcfFile)
            .Where(p =>
            {
                // Keep only SNP lines (REF and ALT are single bases)
                var line = File.ReadLines(vcfFile)
                    .Where(l => !l.StartsWith('#'))
                    .FirstOrDefault(l =>
                    {
                        var f = l.Split('\t');
                        return f.Length >= 2 && int.TryParse(f[1], out var pos) && pos == p;
                    });
                if (line == null)
                {
                    return false;
                }

                var fields = line.Split('\t');
                return fields.Length >= 5 && fields[3].Length == 1 && fields[4].Length == 1;
            })
            .ToHashSet();

        _ctx["ExternalVariantPositions"] = positions;
        _ctx["ExternalCallerName"] = "freebayes";

        if (File.Exists(vcfFile))
        {
            File.Delete(vcfFile);
        }
    }

    [When("samtools mpileup piped to bcftools calls variants from the same BAM")]
    public void WhenSamtoolsBcftoolsCallsVariants()
    {
        var tempDir = TempDir;
        var refFile = (string)_ctx["ReferenceFile"];
        var bamFile = (string)_ctx["BamFile"];
        var vcfFile = TempPath(tempDir, $"bcftools_{Guid.NewGuid():N}.vcf");

        var exit = ExternalToolRunner.Shell(
            $"bcftools mpileup -Ou -f \"{refFile}\" \"{bamFile}\" 2> {ExternalToolRunner.NullDevice} | " +
            $"bcftools call -mv -Ov - > \"{vcfFile}\" 2> {ExternalToolRunner.NullDevice}",
            tempDir, 300_000);

        if (exit != 0)
        {
            throw new InvalidOperationException($"bcftools mpileup | bcftools call failed with exit code {exit}");
        }

        var snpPositions = new HashSet<int>();
        foreach (var line in File.ReadLines(vcfFile))
        {
            if (line.StartsWith('#'))
            {
                continue;
            }

            var fields = line.Split('\t');
            if (fields.Length >= 5
                && int.TryParse(fields[1], out var pos)
                && fields[3].Length == 1
                && fields[4].Length == 1)
            {
                snpPositions.Add(pos);
            }
        }

        _ctx["ExternalVariantPositions"] = snpPositions;
        _ctx["ExternalCallerName"] = "samtools mpileup | bcftools call";

        if (File.Exists(vcfFile))
        {
            File.Delete(vcfFile);
        }
    }

    // ── Then: Alignment assertions ────────────────────────────────────────────

    [Then("the BioSharp mapped count should be within {int} percent of the bwa mem mapped count")]
    public void ThenBioSharpMappedCountWithinTolerance_Bwa(int tolerancePct)
    {
        var biosharpCount = (int)_ctx["BioSharpMappedCount"];
        var externalCount = (int)_ctx["ExternalMappedCount"];
        var parameters = _ctx.TryGetValue("ScenarioParams", out var p) ? (string)p! : "";

        AssertWithinTolerance(biosharpCount, externalCount, tolerancePct,
            "Alignment", "bwa mem", parameters, "MappedReads");
    }

    [Then("the BioSharp mapped count should be within {int} percent of the bwa-mem2 mapped count")]
    public void ThenBioSharpMappedCountWithinTolerance_BwaMem2(int tolerancePct)
    {
        var biosharpCount = (int)_ctx["BioSharpMappedCount"];
        var externalCount = (int)_ctx["ExternalMappedCount"];
        var parameters = _ctx.TryGetValue("ScenarioParams", out var p) ? (string)p! : "";

        AssertWithinTolerance(biosharpCount, externalCount, tolerancePct,
            "Alignment", "bwa-mem2", parameters, "MappedReads");
    }

    // ── Then: Adapter trimming assertions ─────────────────────────────────────

    [Then("the BioSharp surviving read count should be within {int} percent of the fastp surviving count")]
    public void ThenBioSharpSurvivingCountWithinTolerance_Fastp(int tolerancePct)
    {
        var biosharpCount = (int)_ctx["BioSharpSurvivingCount"];
        var externalCount = (int)_ctx["ExternalSurvivingCount"];
        var parameters = _ctx.TryGetValue("ScenarioParams", out var p) ? (string)p! : "";

        AssertWithinTolerance(biosharpCount, externalCount, tolerancePct,
            "AdapterTrimming", "fastp", parameters, "SurvivingReads");
    }

    [Then("the BioSharp bases removed should be within {int} percent of the fastp bases removed")]
    public void ThenBioSharpBasesRemovedWithinTolerance_Fastp(int tolerancePct)
    {
        var biosharpBases = (int)_ctx["BioSharpBasesRemoved"];
        var externalBases = (long)_ctx["ExternalBasesRemoved"];
        var parameters = _ctx.TryGetValue("ScenarioParams", out var p) ? (string)p! : "";

        AssertWithinTolerance(biosharpBases, externalBases, tolerancePct,
            "AdapterTrimming", "fastp", parameters, "BasesRemoved");
    }

    [Then("the BioSharp surviving read count should be within {int} percent of the cutadapt surviving count")]
    public void ThenBioSharpSurvivingCountWithinTolerance_Cutadapt(int tolerancePct)
    {
        var biosharpCount = (int)_ctx["BioSharpSurvivingCount"];
        var externalCount = (int)_ctx["ExternalSurvivingCount"];
        var parameters = _ctx.TryGetValue("ScenarioParams", out var p) ? (string)p! : "";

        AssertWithinTolerance(biosharpCount, externalCount, tolerancePct,
            "AdapterTrimming", "cutadapt", parameters, "SurvivingReads");
    }

    [Then("the BioSharp bases removed should be within {int} percent of the cutadapt bases removed")]
    public void ThenBioSharpBasesRemovedWithinTolerance_Cutadapt(int tolerancePct)
    {
        var biosharpBases = (int)_ctx["BioSharpBasesRemoved"];
        var externalBases = (long)_ctx["ExternalBasesRemoved"];
        var parameters = _ctx.TryGetValue("ScenarioParams", out var p) ? (string)p! : "";

        AssertWithinTolerance(biosharpBases, externalBases, tolerancePct,
            "AdapterTrimming", "cutadapt", parameters, "BasesRemoved");
    }

    [Then("the BioSharp surviving read count should be within {int} percent of the trimmomatic surviving count")]
    public void ThenBioSharpSurvivingCountWithinTolerance_Trimmomatic(int tolerancePct)
    {
        var biosharpCount = (int)_ctx["BioSharpSurvivingCount"];
        var externalCount = (int)_ctx["ExternalSurvivingCount"];
        var parameters = _ctx.TryGetValue("ScenarioParams", out var p) ? (string)p! : "";

        AssertWithinTolerance(biosharpCount, externalCount, tolerancePct,
            "AdapterTrimming", "trimmomatic", parameters, "SurvivingReads");
    }

    [Then("the BioSharp bases removed should be within {int} percent of the trimmomatic bases removed")]
    public void ThenBioSharpBasesRemovedWithinTolerance_Trimmomatic(int tolerancePct)
    {
        var biosharpBases = (int)_ctx["BioSharpBasesRemoved"];
        var externalBases = (long)_ctx["ExternalBasesRemoved"];
        var parameters = _ctx.TryGetValue("ScenarioParams", out var p) ? (string)p! : "";

        AssertWithinTolerance(biosharpBases, externalBases, tolerancePct,
            "AdapterTrimming", "trimmomatic", parameters, "BasesRemoved");
    }

    // ── Then: QC assertions ───────────────────────────────────────────────────

    [Then("the BioSharp total read count should match the fastqc total read count exactly")]
    public void ThenBioSharpTotalReadCountMatchesFastqc()
    {
        var report = (FastQReport)_ctx["BioSharpReport"];
        var fastqcTotal = (long)_ctx["FastqcTotalReads"];
        var parameters = _ctx.TryGetValue("ScenarioParams", out var p) ? (string)p! : "";

        EquivalencyResultCollector.Add(new EquivalencyResult(
            "QualityControl", "fastqc", parameters, "TotalReads",
            report.TotalReads, fastqcTotal, 0, report.TotalReads == fastqcTotal));

        Assert.Equal(fastqcTotal, report.TotalReads);
    }

    [Then("the BioSharp mean per-base quality should be within {int} percent of the fastqc mean quality")]
    public void ThenBioSharpMeanQualityWithinTolerance(int tolerancePct)
    {
        var report = (FastQReport)_ctx["BioSharpReport"];
        var fastqcMean = (double)_ctx["FastqcMeanQuality"];
        var parameters = _ctx.TryGetValue("ScenarioParams", out var p) ? (string)p! : "";

        var biosharpMean = report.PerBaseQuality.Count > 0
            ? report.PerBaseQuality.Values.Average(s => s.Mean)
            : 0.0;

        AssertWithinTolerance(biosharpMean, fastqcMean, tolerancePct,
            "QualityControl", "fastqc", parameters, "MeanPerBaseQuality");
    }

    // ── Then: Variant calling assertions ──────────────────────────────────────

    [Then("the BioSharp SNP positions should overlap freebayes SNP positions by at least {int} percent")]
    public void ThenBioSharpOverlapsFreebayes(int minOverlapPct)
    {
        var biosharpPositions = (HashSet<int>)_ctx["BioSharpVariantPositions"];
        var externalPositions = (HashSet<int>)_ctx["ExternalVariantPositions"];
        var parameters = _ctx.TryGetValue("ScenarioParams", out var p) ? (string)p! : "";

        var overlapPct = JaccardOverlapPct(biosharpPositions, externalPositions);

        EquivalencyResultCollector.Add(new EquivalencyResult(
            "VariantCalling", "freebayes", parameters, "SNPPositionOverlap%",
            overlapPct, 100.0, minOverlapPct, overlapPct >= minOverlapPct));

        Assert.True(overlapPct >= minOverlapPct,
            $"[VariantCalling] BioSharp SNP Jaccard overlap with freebayes was {overlapPct:F1}%, " +
            $"below the required {minOverlapPct}%. " +
            $"BioSharp positions: [{string.Join(",", biosharpPositions.OrderBy(x => x).Take(20))}], " +
            $"freebayes positions: [{string.Join(",", externalPositions.OrderBy(x => x).Take(20))}]. " +
            $"Parameters: {parameters}");
    }

    [Then("the BioSharp SNP positions should overlap samtools-bcftools SNP positions by at least {int} percent")]
    public void ThenBioSharpOverlapsSamtoolsBcftools(int minOverlapPct)
    {
        var biosharpPositions = (HashSet<int>)_ctx["BioSharpVariantPositions"];
        var externalPositions = (HashSet<int>)_ctx["ExternalVariantPositions"];
        var parameters = _ctx.TryGetValue("ScenarioParams", out var p) ? (string)p! : "";

        var overlapPct = JaccardOverlapPct(biosharpPositions, externalPositions);

        EquivalencyResultCollector.Add(new EquivalencyResult(
            "VariantCalling", "samtools mpileup | bcftools call", parameters, "SNPPositionOverlap%",
            overlapPct, 100.0, minOverlapPct, overlapPct >= minOverlapPct));

        Assert.True(overlapPct >= minOverlapPct,
            $"[VariantCalling] BioSharp SNP Jaccard overlap with samtools|bcftools was {overlapPct:F1}%, " +
            $"below the required {minOverlapPct}%. " +
            $"BioSharp positions: [{string.Join(",", biosharpPositions.OrderBy(x => x).Take(20))}], " +
            $"samtools|bcftools positions: [{string.Join(",", externalPositions.OrderBy(x => x).Take(20))}]. " +
            $"Parameters: {parameters}");
    }

    // ── Given: Synthetic BCL run directory ────────────────────────────────────

    /// <summary>
    /// Creates a minimal deterministic Illumina run folder in a temp directory:
    /// 1 lane, 1 tile (1101), N cycles, M clusters per tile, all passing filter.
    /// </summary>
    [Given("a synthetic Illumina BCL run directory with {int} cycles and {int} clusters per tile")]
    public void GivenSyntheticBclRunDirectory(int cycleCount, int clusterCount)
    {
        var tempDir = TempDir;
        var runDir = TempPath(tempDir, "bcl_run");
        Directory.CreateDirectory(runDir);

        const int laneCount = 1;
        const int firstTile = 1101;

        var baseCallsLaneDir = Path.Combine(runDir, "Data", "Intensities", "BaseCalls", "L001");
        var intensitiesLaneDir = Path.Combine(runDir, "Data", "Intensities", "L001");
        Directory.CreateDirectory(baseCallsLaneDir);
        Directory.CreateDirectory(intensitiesLaneDir);

        // BCL files: one per cycle, gzip-compressed (.bcl.gz).
        // bcl-convert v4.x requires .bcl.gz (or .cbcl); bcl2fastq and BioSharp support both.
        for (var cycle = 1; cycle <= cycleCount; cycle++)
        {
            var cycleDir = Path.Combine(baseCallsLaneDir, $"C{cycle}.1");
            Directory.CreateDirectory(cycleDir);
            WriteBclFile(Path.Combine(cycleDir, $"s_1_{firstTile}.bcl.gz"), clusterCount, cycle);
        }

        // Filter file: per-tile (bcl2fastq requires s_{lane}_{tile}.filter)
        WriteFilterFile(Path.Combine(baseCallsLaneDir, $"s_1_{firstTile}.filter"), clusterCount);

        // Position file
        WriteLocsFile(Path.Combine(intensitiesLaneDir, $"s_1_{firstTile}.locs"), clusterCount);

        // InterOp directory (bcl2fastq requires this directory to exist)
        Directory.CreateDirectory(Path.Combine(runDir, "InterOp"));

        // Completion sentinel files (bcl-convert 4.x requires these)
        File.WriteAllText(Path.Combine(runDir, "RTAComplete.txt"), "RTA 2.11.3 Complete\n");
        File.WriteAllText(Path.Combine(runDir, "CopyComplete.txt"), "");

        // RunInfo.xml
        BclWriteRunInfoXml(Path.Combine(runDir, "RunInfo.xml"), cycleCount, laneCount, firstTile);

        // bcl2fastq sample sheet (IEM v4)
        WriteBcl2FastqSampleSheet(Path.Combine(runDir, "SampleSheet.csv"), cycleCount);

        // bcl-convert sample sheet (v2 format)
        WriteBclConvertSampleSheet(Path.Combine(runDir, "SampleSheet.bcl-convert.csv"), cycleCount);

        _ctx["BclRunDir"] = runDir;
        _ctx["BclCycleCount"] = cycleCount;
        _ctx["BclClusterCount"] = clusterCount;
        _ctx["ScenarioParams"] = $"cycles={cycleCount},clusters={clusterCount}";
    }

    // ── When: BioSharp BCL→FASTQ ──────────────────────────────────────────────

    [When("BioSharp converts the BCL run directory to FASTQ")]
    public async Task WhenBioSharpConvertsBclRunDirectoryToFastq()
    {
        var tempDir = TempDir;
        var runDir = (string)_ctx["BclRunDir"];
        var cycleCount = (int)_ctx["BclCycleCount"];
        var outDir = TempPath(tempDir, $"biosharp_bcl_out_{Guid.NewGuid():N}");
        Directory.CreateDirectory(outDir);

        var reader = new IlluminaDataReader(
            new DirectoryInfo(runDir),
            NullLoggerFactory.Instance,
            ReadStructure.Parse($"{cycleCount}T"));

        _ = reader.RunInfo();

        foreach (var lane in reader.GetAllLanes())
        {
            var outFastq = TempPath(outDir, $"biosharp_L{lane:D3}.fastq.gz");
            var outIndex = TempPath(outDir, $"biosharp_L{lane:D3}.fastqi.gz");

            await using var fastqFile = File.Open(
                outFastq,
                new FileStreamOptions
                {
                    Access = FileAccess.Write,
                    Mode = FileMode.Create,
                    Options = FileOptions.Asynchronous,
                    Share = FileShare.None
                });
            await using var indexFile = File.Open(
                outIndex,
                new FileStreamOptions
                {
                    Access = FileAccess.Write,
                    Mode = FileMode.Create,
                    Options = FileOptions.Asynchronous,
                    Share = FileShare.None
                });
            await using var writer = new FastQWriter(
                NullLogger.Instance, fastqFile, indexFile,
                CompressionLevel.Fastest, leaveOpen: true);

            await foreach (var sampleReader in reader.ReadClusterData(lane, CancellationToken.None).ConfigureAwait(false))
            {
                await using var sr = sampleReader;
                await writer.Write(
                    sr.ReadBclData(DefaultQualityTrimmer.Instance, CancellationToken.None),
                    CancellationToken.None).ConfigureAwait(false);
            }
        }

        var summary = BclSummarizeFastqDirectory(outDir);
        _ctx["BioSharpBclReadCount"] = summary.ReadCount;
        _ctx["BioSharpBclTotalBases"] = summary.TotalBases;

        // Clean up immediately to avoid disk pressure
        Directory.Delete(outDir, recursive: true);
    }

    // ── When: External BCL converters ────────────────────────────────────────

    [When("bcl-convert converts the same BCL run directory to FASTQ")]
    public void WhenBclConvertConvertsBclRun()
    {
        var tempDir = TempDir;
        var runDir = (string)_ctx["BclRunDir"];
        var outDir = TempPath(tempDir, $"bclconvert_out_{Guid.NewGuid():N}");
        Directory.CreateDirectory(outDir);
        var logFile = TempPath(tempDir, "bcl-convert.log");
        var sampleSheet = Path.Combine(runDir, "SampleSheet.bcl-convert.csv");

        var command =
            $"bcl-convert --bcl-input-directory \"{runDir}\"" +
            $" --output-directory \"{outDir}\"" +
            $" --sample-sheet \"{sampleSheet}\"" +
            $" --bcl-num-conversion-threads {Threads}" +
            $" --bcl-num-compression-threads {Threads}" +
            " --bcl-sampleproject-subdirectories false" +
            " --force" +
            $" > {ExternalToolRunner.NullDevice} 2>\"{logFile}\"";

        var exit = ExternalToolRunner.Shell(command, tempDir, 120_000);
        if (exit != 0)
        {
            var log = File.Exists(logFile) ? File.ReadAllText(logFile) : "(no log)";
            var snippet = log.Length > 2000 ? log[..2000] : log;
            throw new InvalidOperationException(
                $"bcl-convert exited with code {exit}. STDERR:\n{snippet}");
        }

        var summary = BclSummarizeFastqDirectory(outDir);
        if (summary.FastqFileCount == 0 || summary.ReadCount == 0)
        {
            throw new InvalidOperationException(
                "bcl-convert completed but produced no FASTQ reads — cannot perform equivalency comparison.");
        }

        _ctx["ExternalBclReadCount"] = summary.ReadCount;
        _ctx["ExternalBclTotalBases"] = summary.TotalBases;
        _ctx["ExternalBclConverterName"] = "bcl-convert";

        Directory.Delete(outDir, recursive: true);
        if (File.Exists(logFile))
        {
            File.Delete(logFile);
        }
    }

    [When("bcl2fastq converts the same BCL run directory to FASTQ")]
    public void WhenBcl2FastqConvertsBclRun()
    {
        var tempDir = TempDir;
        var runDir = (string)_ctx["BclRunDir"];
        var outDir = TempPath(tempDir, $"bcl2fastq_out_{Guid.NewGuid():N}");
        Directory.CreateDirectory(outDir);
        var logFile = TempPath(tempDir, "bcl2fastq.log");
        var sampleSheet = Path.Combine(runDir, "SampleSheet.csv");

        var command =
            $"bcl2fastq --runfolder-dir \"{runDir}\"" +
            $" --output-dir \"{outDir}\"" +
            $" --sample-sheet \"{sampleSheet}\"" +
            $" --loading-threads {Threads}" +
            $" --processing-threads {Threads}" +
            $" --writing-threads {Threads}" +
            " --no-lane-splitting" +
            " --ignore-missing-bcls" +
            " --ignore-missing-filter" +
            " --ignore-missing-positions" +
            $" > {ExternalToolRunner.NullDevice} 2>\"{logFile}\"";

        var exit = ExternalToolRunner.Shell(command, tempDir, 120_000);
        if (exit != 0)
        {
            var log = File.Exists(logFile) ? File.ReadAllText(logFile) : "(no log)";
            var snippet = log.Length > 2000 ? log[..2000] : log;
            throw new InvalidOperationException(
                $"bcl2fastq exited with code {exit}. STDERR:\n{snippet}");
        }

        var summary = BclSummarizeFastqDirectory(outDir);
        if (summary.FastqFileCount == 0 || summary.ReadCount == 0)
        {
            throw new InvalidOperationException(
                "bcl2fastq completed but produced no FASTQ reads — cannot perform equivalency comparison.");
        }

        _ctx["ExternalBclReadCount"] = summary.ReadCount;
        _ctx["ExternalBclTotalBases"] = summary.TotalBases;
        _ctx["ExternalBclConverterName"] = "bcl2fastq";

        Directory.Delete(outDir, recursive: true);
        if (File.Exists(logFile))
        {
            File.Delete(logFile);
        }
    }

    // ── Then: BCL equivalency assertions ─────────────────────────────────────

    [Then("the BioSharp BCL read count should exactly match the external converter read count")]
    public void ThenBclReadCountMatchesExactly()
    {
        var biosharpCount = (long)_ctx["BioSharpBclReadCount"];
        var externalCount = (long)_ctx["ExternalBclReadCount"];
        var converterName = (string)_ctx["ExternalBclConverterName"];
        var parameters = _ctx.TryGetValue("ScenarioParams", out var p) ? (string)p! : "";

        EquivalencyResultCollector.Add(new EquivalencyResult(
            "BclConversion", converterName, parameters, "ReadCount",
            biosharpCount, externalCount, 0, biosharpCount == externalCount));

        Assert.True(biosharpCount == externalCount,
            $"[BclConversion] BioSharp produced {biosharpCount} reads but {converterName} produced " +
            $"{externalCount} reads. Parameters: {parameters}");
    }

    [Then("the BioSharp BCL total bases should exactly match the external converter total bases")]
    public void ThenBclTotalBasesMatchExactly()
    {
        var biosharpBases = (long)_ctx["BioSharpBclTotalBases"];
        var externalBases = (long)_ctx["ExternalBclTotalBases"];
        var converterName = (string)_ctx["ExternalBclConverterName"];
        var parameters = _ctx.TryGetValue("ScenarioParams", out var p) ? (string)p! : "";

        EquivalencyResultCollector.Add(new EquivalencyResult(
            "BclConversion", converterName, parameters, "TotalBases",
            biosharpBases, externalBases, 0, biosharpBases == externalBases));

        Assert.True(biosharpBases == externalBases,
            $"[BclConversion] BioSharp produced {biosharpBases} bases but {converterName} produced " +
            $"{externalBases} bases. Parameters: {parameters}");
    }

    // ── BCL helper: synthetic run folder writers ──────────────────────────────

    private static void WriteBclFile(string path, int clusterCount, int cycle)
    {
        // BCL payload: 4-byte cluster count header + 1 byte per cluster (quality<<2 | base).
        // Written as gzip-compressed (.bcl.gz) so bcl-convert v4.x can detect the format.
        // BioSharp's BclReader and bcl2fastq also handle .bcl.gz transparently.
        var payload = new byte[4 + clusterCount];
        BitConverter.TryWriteBytes(payload.AsSpan(0, 4), clusterCount);
        for (var i = 0; i < clusterCount; i++)
        {
            var quality  = 30 + ((i + cycle) % 8);
            var baseCode = (i + cycle) & 0x03;
            payload[4 + i] = (byte)((quality << 2) | baseCode);
        }

        using var fs = File.Create(path);
        using var gz = new GZipStream(fs, CompressionLevel.Fastest, leaveOpen: false);
        gz.Write(payload);
    }

    private static void WriteFilterFile(string path, int clusterCount)
    {
        var payload = new byte[12 + clusterCount];
        // bytes 0..3 = 0 (already zero)
        BitConverter.TryWriteBytes(payload.AsSpan(4, 4), FilterFileReader.ExpectedVersion);
        BitConverter.TryWriteBytes(payload.AsSpan(8, 4), clusterCount);
        payload.AsSpan(12, clusterCount).Fill(0x01); // all clusters pass filter
        File.WriteAllBytes(path, payload);
    }

    private static void WriteLocsFile(string path, int clusterCount)
    {
        using var fs = File.Create(path);
        using var writer = new BinaryWriter(fs, Encoding.UTF8, leaveOpen: false);
        writer.Write(1);             // magic int32 = 1
        writer.Write(1.0f);          // version float32 = 1.0
        writer.Write(clusterCount);  // number of clusters
        for (var i = 0; i < clusterCount; i++)
        {
            writer.Write((float)(1000 + i));  // x coordinate (float32) — locs spec requires IEEE 754 single-precision
            writer.Write((float)(2000 + i));  // y coordinate (float32)
        }
    }

    private static void BclWriteRunInfoXml(string path, int cycleCount, int laneCount, int firstTile = 1101)
    {
        // Minimal RunInfo.xml matching the real sampledata format (no TileSet section).
        // bcl-convert will discover tiles by scanning the BaseCalls directory.
        // Do NOT include TileNamingConvention — the synthetic instrument 'Equiv' combined
        // with FiveDigit tile naming confuses bcl-convert's format detection.
        File.WriteAllText(path, $"""
            <?xml version="1.0"?>
            <RunInfo xmlns:xsd="http://www.w3.org/2001/XMLSchema"
                     xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" Version="2">
              <Run Id="BioSharpEquivRun" Number="1">
                <Flowcell>EQUIV</Flowcell>
                <Instrument>D00547</Instrument>
                <Date>20260101</Date>
                <Reads>
                  <Read Number="1" NumCycles="{cycleCount}" IsIndexedRead="N" />
                </Reads>
                <FlowcellLayout LaneCount="{laneCount}" SurfaceCount="1" SwathCount="1" TileCount="1" />
              </Run>
            </RunInfo>
            """);
    }

    private static void WriteBcl2FastqSampleSheet(string path, int cycleCount)
    {
        File.WriteAllText(path, $"""
            [Header]
            IEMFileVersion,4
            Experiment Name,BioSharpEquivalency
            Date,2026-01-01
            Workflow,GenerateFASTQ
            Application,FASTQ Only
            Instrument Type,NextSeq

            [Reads]
            {cycleCount}

            [Data]
            Sample_ID
            BenchSample
            """);
    }

    private static void WriteBclConvertSampleSheet(string path, int cycleCount)
    {
        // bcl-convert 4.0.x requires [BCLConvert_Settings] to contain at least one
        // key-value pair; an empty section header is rejected with "must have a
        // [BCLConvert_Settings] section".  Use OverrideCycles to state the cycle
        // structure explicitly (Y = yield/read cycles, matches our synthetic BCL data).
        // 'Read1Cycles' was tried but rejected as "unrecognized" by the 4.0.3 binary.
        File.WriteAllText(path, $"""
            [Header]
            FileFormatVersion,2

            [BCLConvert_Settings]
            OverrideCycles,Y{cycleCount}

            [BCLConvert_Data]
            Sample_ID
            BenchSample
            """);
    }

    // ── BCL helper: summarise FASTQ output directory ─────────────────────────

    private record BclFastqSummary(int FastqFileCount, long ReadCount, long TotalBases);

    private static BclFastqSummary BclSummarizeFastqDirectory(string dir)
    {
        var fastqFiles = Directory.EnumerateFiles(dir, "*.fastq.gz", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        long readCount = 0;
        long totalBases = 0;

        foreach (var file in fastqFiles)
        {
            using var fs = File.OpenRead(file);
            using var gzip = new GZipStream(fs, CompressionMode.Decompress);
            using var sr = new StreamReader(gzip, Encoding.UTF8, leaveOpen: false);

            string? line;
            var lineIndex = 0;
            while ((line = sr.ReadLine()) != null)
            {
                switch (lineIndex % 4)
                {
                    case 1: totalBases += line.Length; break;
                    case 3: readCount++; break;
                }
                lineIndex++;
            }
        }

        return new BclFastqSummary(fastqFiles.Length, readCount, totalBases);
    }

    // ── Annotation (SnpEff equivalency) ───────────────────────────────────────

    [Given("a synthetic reference with {int} transcripts and {int} planted variants")]
    public void GivenSyntheticReferenceWithTranscriptsAndVariants(int transcriptCount, int variantCount)
    {
        var tempDir = TempDir;
        var fastaPath = TempPath(tempDir, "synth_ref.fa");
        var gtfPath = TempPath(tempDir, "synth_genes.gtf");
        var vcfPath = TempPath(tempDir, "synth_variants.vcf");

        const int txLength = 1000;
        const int cdsOffset = 100;
        var sb = new StringBuilder();
        var lines = new List<string>();

        // FASTA: one sequence per transcript
        for (var t = 0; t < transcriptCount; t++)
        {
            var chromName = $"synth{t + 1}";
            sb.Append('>').AppendLine(chromName);
            var seq = GenerateSyntheticNucleotides(txLength);
            sb.AppendLine(seq);
            lines.Add($"##contig=<ID={chromName},length={txLength}>");
        }

        File.WriteAllText(fastaPath, sb.ToString());

        // GTF: one transcript + CDS + exon per chromosome
        using (var gtfWriter = File.CreateText(gtfPath))
        {
            for (var t = 0; t < transcriptCount; t++)
            {
                var chrom = $"synth{t + 1}";
                var txId = $"TX{t + 1:D3}";
                var cdsEnd = txLength - cdsOffset;
                gtfWriter.WriteLine($"{chrom}\tSynth\ttranscript\t1\t{txLength}\t.\t+\t.\tgene_id \"{txId}\"; transcript_id \"{txId}\";");
                gtfWriter.WriteLine($"{chrom}\tSynth\texon\t1\t{txLength}\t.\t+\t.\tgene_id \"{txId}\"; transcript_id \"{txId}\";");
                gtfWriter.WriteLine($"{chrom}\tSynth\tCDS\t{cdsOffset}\t{cdsEnd}\t.\t+\t0\tgene_id \"{txId}\"; transcript_id \"{txId}\";");
            }
        }

        // VCF: distribute variants across transcripts
        using (var vcfWriter = File.CreateText(vcfPath))
        {
            vcfWriter.WriteLine("##fileformat=VCFv4.1");
            foreach (var contig in lines)
            {
                vcfWriter.WriteLine(contig);
            }

            vcfWriter.WriteLine("#CHROM\tPOS\tID\tREF\tALT\tQUAL\tFILTER\tINFO");

            var positions = new HashSet<string>();
            for (var v = 0; v < variantCount; v++)
            {
                var tIdx = v % transcriptCount;
                var chrom = $"synth{tIdx + 1}";
                var pos = 50 + (v / transcriptCount * 3) + (tIdx * 10) + 1;
                var key = $"{chrom}:{pos}";
                if (!positions.Add(key))
                {
                    pos += variantCount;
                    positions.Add($"{chrom}:{pos}");
                }

                var refBase = "A";
                var altBase = v % 4 == 0 ? "T" : v % 4 == 1 ? "C" : v % 4 == 2 ? "G" : "T";
                vcfWriter.WriteLine($"{chrom}\t{pos}\t.\t{refBase}\t{altBase}\t30\tPASS\t.");
            }
        }

        _ctx["SynthFastaPath"] = fastaPath;
        _ctx["SynthGtfPath"] = gtfPath;
        _ctx["SynthVcfPath"] = vcfPath;
    }

    [When("BioSharp annotates the variants using the synthetic GTF transcripts")]
    public async Task WhenBioSharpAnnotatesVariants()
    {
        var gtfPath = (string)_ctx["SynthGtfPath"];
        var vcfPath = (string)_ctx["SynthVcfPath"];

        var engine = new VariantAnnotationEngine();
        await engine.LoadTranscriptsFromGtf(gtfPath, CancellationToken.None).ConfigureAwait(false);

        var annotations = new List<VariantAnnotation>();
        await foreach (var ann in engine.AnnotateVcf(vcfPath).ConfigureAwait(false))
        {
            annotations.Add(ann);
        }

        var codingConsequences = new HashSet<VariantConsequence>
        {
            VariantConsequence.Missense,
            VariantConsequence.Nonsense,
            VariantConsequence.Synonymous,
            VariantConsequence.Frameshift,
            VariantConsequence.InframeInsertion,
            VariantConsequence.InframeDeletion
        };
        var codingCount = annotations.Count(a => codingConsequences.Contains(a.Consequence));
        var codingFraction = annotations.Count > 0 ? (double)codingCount / annotations.Count : 0.0;

        _ctx["BioSharpAnnotatedCount"] = annotations.Count;
        _ctx["BioSharpCodingFraction"] = codingFraction;
    }

    [When("SnpEff annotates the same variants using a custom database built from the synthetic GTF")]
    public void WhenSnpEffAnnotatesVariants()
    {
        var tempDir = TempDir;
        var fastaPath = (string)_ctx["SynthFastaPath"];
        var gtfPath = (string)_ctx["SynthGtfPath"];
        var vcfPath = (string)_ctx["SynthVcfPath"];

        var snpeffData = TempPath(tempDir, "snpeff_data");
        Directory.CreateDirectory(Path.Combine(snpeffData, "synth"));

        File.Copy(fastaPath, Path.Combine(snpeffData, "synth", "sequences.fa"), overwrite: true);
        File.Copy(gtfPath, Path.Combine(snpeffData, "synth", "genes.gtf"), overwrite: true);

        var configPath = TempPath(tempDir, "snpEff.config");
        File.WriteAllText(configPath, $"data.dir = {snpeffData}\nsynth.genome : Synthetic\n");

        // Build custom SnpEff database
        var (buildExit, buildOutput) = ExternalToolRunner.ShellCapture(
            $"snpeff build -gtf22 -c \"{configPath}\" -v synth 2>&1",
            tempDir, 180_000);
        if (buildExit != 0)
        {
            throw new InvalidOperationException(
                $"snpeff build failed (exit {buildExit}). Output: {buildOutput[..Math.Min(500, buildOutput.Length)]}");
        }

        // Annotate VCF
        var outVcf = TempPath(tempDir, $"snpeff_out_{Guid.NewGuid():N}.vcf");
        var (annExit, annOutput) = ExternalToolRunner.ShellCapture(
            $"snpeff ann -c \"{configPath}\" -noLog synth \"{vcfPath}\" > \"{outVcf}\" 2>&1",
            tempDir, 180_000);
        if (annExit != 0)
        {
            throw new InvalidOperationException(
                $"snpeff ann failed (exit {annExit}). Output: {annOutput[..Math.Min(500, annOutput.Length)]}");
        }

        var annotatedCount = 0;
        var codingCount = 0;
        if (File.Exists(outVcf))
        {
            foreach (var line in File.ReadLines(outVcf))
            {
                if (line.StartsWith('#'))
                {
                    continue;
                }

                annotatedCount++;
                // ANN field contains HIGH/MODERATE impact consequences = coding
                var infoIdx = line.Split('\t').Length > 7 ? line.Split('\t')[7] : "";
                if (infoIdx.Contains("|HIGH|", StringComparison.OrdinalIgnoreCase)
                    || infoIdx.Contains("|MODERATE|", StringComparison.OrdinalIgnoreCase))
                {
                    codingCount++;
                }
            }
        }

        var snpeffCodingFraction = annotatedCount > 0 ? (double)codingCount / annotatedCount : 0.0;
        _ctx["SnpEffAnnotatedCount"] = annotatedCount;
        _ctx["SnpEffCodingFraction"] = snpeffCodingFraction;
    }

    [Then("the BioSharp annotated variant count should be within {int} percent of the SnpEff annotated count")]
    public void ThenBioSharpAnnotatedCountWithinTolerance(int tolerancePct)
    {
        var biosharpCount = (int)_ctx["BioSharpAnnotatedCount"];
        var snpeffCount = (int)_ctx["SnpEffAnnotatedCount"];
        var parameters = _ctx.TryGetValue("ScenarioParams", out var p) ? (string)p! : "";

        AssertWithinTolerance(biosharpCount, snpeffCount, tolerancePct,
            "VariantAnnotation", "snpeff", parameters, "AnnotatedVariantCount");
    }

    [Then("the BioSharp coding consequence fraction should be within {int} percent of the SnpEff coding fraction")]
    public void ThenBioSharpCodingFractionWithinTolerance(int tolerancePct)
    {
        var biosharpFraction = (double)_ctx["BioSharpCodingFraction"];
        var snpeffFraction = (double)_ctx["SnpEffCodingFraction"];
        var parameters = _ctx.TryGetValue("ScenarioParams", out var p) ? (string)p! : "";

        AssertWithinTolerance(biosharpFraction, snpeffFraction, tolerancePct,
            "VariantAnnotation", "snpeff", parameters, "CodingConsequenceFraction");
    }

    private static string GenerateSyntheticNucleotides(int length)
    {
        var bases = "ACGT";
        var sb = new StringBuilder(length);
        for (var i = 0; i < length; i++)
        {
            sb.Append(bases[i % 4]);
        }

        return sb.ToString();
    }
}
