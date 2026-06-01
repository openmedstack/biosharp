namespace OpenMedStack.BioSharp.AcceptanceTests.StepDefinitions;

using System;
using System.IO;
using Reqnroll;
using Xunit;

/// <summary>
/// Reqnroll hooks for the Tool Equivalency scenarios.
///
/// <list type="bullet">
///   <item>
///     <description>
///       <c>[BeforeScenario]</c> hooks tagged with the corresponding <c>@RequiresXxx</c>
///       tag skip the scenario when the requested external tool is not available on the
///       current OS / architecture.  This keeps the test suite green on developer machines
///       and non-Linux-x64 CI runners where the bioinformatics tools are not installed.
///     </description>
///   </item>
///   <item>
///     <description>
///       The <c>[AfterTestRun]</c> hook writes the collected equivalency results to a
///       markdown report whose path is controlled by the <c>BIOSHARP_EQUIV_REPORT_PATH</c>
///       environment variable (default: <c>reports/equivalency-report.md</c> relative to
///       the current directory).
///     </description>
///   </item>
/// </list>
/// </summary>
[Binding]
public sealed class ToolEquivalencyHooks
{
    private readonly ScenarioContext _ctx;

    public ToolEquivalencyHooks(ScenarioContext ctx)
    {
        _ctx = ctx;
    }

    // ── Per-tool skip guards ─────────────────────────────────────────────────

    [BeforeScenario("RequiresBwa")]
    public void RequiresBwa()
    {
        if (!ExternalToolRunner.IsAvailable("bwa"))
        {
            Assert.Skip("bwa is not available on this platform/architecture — skipping equivalency scenario.");
        }
    }

    [BeforeScenario("RequiresBwaMem2")]
    public void RequiresBwaMem2()
    {
        if (!ExternalToolRunner.IsAvailable("bwa-mem2"))
        {
            Assert.Skip("bwa-mem2 is not available on this platform/architecture — skipping equivalency scenario.");
        }
    }

    [BeforeScenario("RequiresSamtools")]
    public void RequiresSamtools()
    {
        if (!ExternalToolRunner.IsAvailable("samtools"))
        {
            Assert.Skip("samtools is not available on this platform/architecture — skipping equivalency scenario.");
        }
    }

    [BeforeScenario("RequiresBcftools")]
    public void RequiresBcftools()
    {
        if (!ExternalToolRunner.IsAvailable("bcftools"))
        {
            Assert.Skip("bcftools is not available on this platform/architecture — skipping equivalency scenario.");
        }
    }

    [BeforeScenario("RequiresFreebayes")]
    public void RequiresFreebayes()
    {
        if (!ExternalToolRunner.IsAvailable("freebayes"))
        {
            Assert.Skip("freebayes is not available on this platform/architecture — skipping equivalency scenario.");
        }
    }

    [BeforeScenario("RequiresFastp")]
    public void RequiresFastp()
    {
        if (!ExternalToolRunner.IsAvailable("fastp"))
        {
            Assert.Skip("fastp is not available on this platform/architecture — skipping equivalency scenario.");
        }
    }

    [BeforeScenario("RequiresCutadapt")]
    public void RequiresCutadapt()
    {
        if (!ExternalToolRunner.IsAvailable("cutadapt"))
        {
            Assert.Skip("cutadapt is not available on this platform/architecture — skipping equivalency scenario.");
        }
    }

    [BeforeScenario("RequiresFastqc")]
    public void RequiresFastqc()
    {
        if (!ExternalToolRunner.IsAvailable("fastqc"))
        {
            Assert.Skip("fastqc is not available on this platform/architecture — skipping equivalency scenario.");
        }
    }

    [BeforeScenario("RequiresBclConvert")]
    public void RequiresBclConvert()
    {
        if (!ExternalToolRunner.IsAvailable("bcl-convert"))
        {
            Assert.Skip("bcl-convert is not available on this platform/architecture — skipping BCL equivalency scenario.");
        }
    }

    [BeforeScenario("RequiresBcl2Fastq")]
    public void RequiresBcl2Fastq()
    {
        if (!ExternalToolRunner.IsAvailable("bcl2fastq"))
        {
            Assert.Skip("bcl2fastq is not available on this platform/architecture — skipping BCL equivalency scenario.");
        }
    }

    [BeforeScenario("RequiresTrimmomatic")]
    public void RequiresTrimmomatic()
    {
        if (!ExternalToolRunner.IsAvailable("trimmomatic"))
        {
            Assert.Skip("trimmomatic is not available on this platform/architecture — skipping equivalency scenario.");
        }
    }

    [BeforeScenario("RequiresSnpEff")]
    public void RequiresSnpEff()
    {
        if (!ExternalToolRunner.IsAvailable("snpeff"))
        {
            Assert.Skip("snpeff is not available on this platform/architecture — skipping equivalency scenario.");
        }
    }

    // ── Scenario-level temp directory management ────────────────────────────

    [BeforeScenario("Equivalency")]
    public void InitializeTempDirectory()
    {
        var tempDir = Path.Combine(
            Path.GetTempPath(),
            $"biosharp-equiv-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        _ctx["ScenarioTempDir"] = tempDir;
        // Track result count at start so AfterScenario can detect un-recorded failures.
        _ctx["ResultCountAtScenarioStart"] = EquivalencyResultCollector.GetAll().Count;
    }

    [AfterScenario("Equivalency")]
    public void CleanupTempDirectory()
    {
        // If the scenario errored before any assertion step added its result, record
        // a synthetic failed entry so the markdown report reflects the true state.
        if (_ctx.TestError != null &&
            _ctx.TryGetValue("ResultCountAtScenarioStart", out var countObj) &&
            countObj is int countAtStart &&
            EquivalencyResultCollector.GetAll().Count == countAtStart)
        {
            var category = DeriveCategory(_ctx.ScenarioInfo.Tags);
            var tool = DeriveTool(_ctx.ScenarioInfo.Tags);
            EquivalencyResultCollector.Add(new EquivalencyResult(
                Category: category,
                ExternalTool: tool,
                Parameters: _ctx.ScenarioInfo.Title,
                Metric: "scenario",
                BioSharpValue: 0,
                ExternalValue: 0,
                TolerancePct: 0,
                Passed: false));
        }

        if (_ctx.TryGetValue("ScenarioTempDir", out var tempDirObj) &&
            tempDirObj is string tempDir &&
            Directory.Exists(tempDir))
        {
            try
            {
                Directory.Delete(tempDir, recursive: true);
            }
            catch
            {
                // Best-effort cleanup; do not fail the test on cleanup errors.
            }
        }
    }

    private static string DeriveCategory(string[] tags) => tags switch
    {
        _ when Array.Exists(tags, t => t is "RequiresBclConvert" or "RequiresBcl2Fastq") => "BclConversion",
        _ when Array.Exists(tags, t => t is "RequiresBwa" or "RequiresBwaMem2") => "Alignment",
        _ when Array.Exists(tags, t => t is "RequiresSamtools" or "RequiresBcftools" or "RequiresFreebayes") => "VariantCalling",
        _ when Array.Exists(tags, t => t is "RequiresCutadapt" or "RequiresTrimmomatic") => "AdapterTrimming",
        _ when Array.Exists(tags, t => t is "RequiresFastp" or "RequiresFastqc") => "QualityControl",
        _ when Array.Exists(tags, t => t is "RequiresSnpEff") => "VariantAnnotation",
        _ => "Other"
    };

    private static string DeriveTool(string[] tags) => tags switch
    {
        _ when Array.Exists(tags, t => t == "RequiresBclConvert") => "bcl-convert",
        _ when Array.Exists(tags, t => t == "RequiresBcl2Fastq") => "bcl2fastq",
        _ when Array.Exists(tags, t => t == "RequiresBwaMem2") => "bwa-mem2",
        _ when Array.Exists(tags, t => t == "RequiresBwa") => "bwa",
        _ when Array.Exists(tags, t => t == "RequiresFreebayes") => "freebayes",
        _ when Array.Exists(tags, t => t == "RequiresSamtools") => "samtools",
        _ when Array.Exists(tags, t => t == "RequiresBcftools") => "bcftools",
        _ when Array.Exists(tags, t => t == "RequiresCutadapt") => "cutadapt",
        _ when Array.Exists(tags, t => t == "RequiresFastp") => "fastp",
        _ when Array.Exists(tags, t => t == "RequiresFastqc") => "fastqc",
        _ when Array.Exists(tags, t => t == "RequiresTrimmomatic") => "trimmomatic",
        _ when Array.Exists(tags, t => t == "RequiresSnpEff") => "snpeff",
        _ => "unknown"
    };

    // ── Post-run report generation ──────────────────────────────────────────

    [AfterTestRun]
    public static void WriteEquivalencyReport()
    {
        var reportPath = Environment.GetEnvironmentVariable("BIOSHARP_EQUIV_REPORT_PATH")
            ?? Path.Combine("reports", "equivalency-report.md");

        try
        {
            EquivalencyResultCollector.WriteMarkdownReport(reportPath);
            Console.WriteLine($"[ToolEquivalency] Report written to: {Path.GetFullPath(reportPath)}");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[ToolEquivalency] Failed to write report to {reportPath}: {ex.Message}");
        }
    }
}
