namespace OpenMedStack.Preator;

using System;
using System.CommandLine;
using System.IO;

internal static class PreatorCommandOptions
{
    internal static readonly Option<string> InputOption = new("--input", "-i")
    {
        Description = "Set the data input folder (can be relative).",
        Required = true
    };

    internal static readonly Option<string?> FastqOption = new("--fastq", "-fq")
    {
        Description = "Gzipped FASTQ file to analyze."
    };

    internal static readonly Option<string?> FastaOption = new("--fasta", "-fa")
    {
        Description = "FASTA or FASTA.GZ read file to analyze."
    };

    internal static readonly Option<string?> ReferenceIdContainsOption = new("--reference-id-contains", "-ric")
    {
        Description = "Choose a FASTA record by partial ID match."
    };

    internal static readonly Option<string> OutputOption = new("--output", "-o")
    {
        Description = "Set the data output folder (can be relative).",
        DefaultValueFactory = _ => Environment.CurrentDirectory
    };

    internal static readonly Option<string?> ReadStructureOption = new("--readstructure", "-rs")
    {
        Description = "Set the read structure for the data.",
        DefaultValueFactory = _ => null
    };

    internal static readonly Option<string> LanesOption = new("--lanes", "-l")
    {
        Description = "Comma separated list of lanes to read. Use * to read all.",
        DefaultValueFactory = _ => "1"
    };

    internal static readonly Option<string> ReferenceOption = new("--reference", "-r")
    {
        Description = "FASTA or FASTA.GZ reference sequence file.",
        Required = true
    };

    internal static readonly Option<string?> ChromosomeOption = new("--chromosome", "-c")
    {
        Description = "Override the output contig name used in the VCF/report."
    };

    internal static readonly Option<int> MinAlignmentScoreOption = new("--min-alignment-score")
    {
        Description = "Minimum alignment score.",
        DefaultValueFactory = _ => 10
    };

    internal static readonly Option<int> MinVariantQualityOption = new("--min-variant-quality", "-mvq")
    {
        Description = "Minimum variant quality.",
        DefaultValueFactory = _ => 30
    };

    internal static readonly Option<int> MinAlternateObservationCountOption = new("--min-alternate-observation-count")
    {
        Description = "Minimum number of read observations supporting the alternate allele.",
        DefaultValueFactory = _ => 1
    };

    internal static readonly Option<double> MinAlternateFractionOption = new("--min-alternate-fraction")
    {
        Description = "Minimum fraction of covering reads that must support the alternate allele.",
        DefaultValueFactory = _ => 0.0
    };

    internal static readonly Option<bool> DisableSoftclipRealignOption = new("--disable-softclip-realign")
    {
        Description = "Disable soft-clip realignment."
    };

    internal static readonly Option<bool> EnableGraphSvOption = new("--enable-graph-sv")
    {
        Description = "Run full-reference De Bruijn graph analysis."
    };

    internal static readonly Option<int> KmerSizeOption = new("--kmer-size", "-ks")
    {
        Description = "K-mer size.",
        DefaultValueFactory = _ => 15
    };

    internal static readonly Option<int> MinGraphCoverageOption = new("--min-graph-coverage")
    {
        Description = "Minimum graph coverage.",
        DefaultValueFactory = _ => 5
    };

    internal static readonly Option<int> GraphWindowBpOption = new("--graph-window-bp")
    {
        Description = "Graph window size in bp.",
        DefaultValueFactory = _ => 500
    };

    internal static readonly Option<string> OutputPrefixOption = new("--output-prefix")
    {
        Description = "Output filename prefix.",
        DefaultValueFactory = _ => "variants",
        Required = false
    };

    internal static readonly Option<int?> MaxReadsOption = new("--max-reads", "-mr")
    {
        Description = "Stop after this many reads."
    };

    internal static readonly Option<int> MaxCoresOption = new("--max-cores", "-p")
    {
        Description = "Maximum cores to use.",
        DefaultValueFactory = _ => 10
    };

    internal static readonly Option<FileInfo> VcfOption = new("--vcf")
    {
        Description = "Input VCF or VCF.GZ file to annotate.",
        Required = true
    };

    internal static readonly Option<FileInfo?> OptionalDatabaseOption = new("--database")
    {
        Description = "Transcript annotation SQLite database file. When provided in e2e mode, variants are annotated in-process.",
        Required = false
    };

    internal static readonly Option<FileInfo> DatabaseOption = new("--database")
    {
        Description = "Transcript annotation SQLite database file.",
        Required = true
    };

    internal static readonly Option<string?> TranscriptIdOption = new("--transcript-id")
    {
        Description = "Restrict annotation to a single transcript ID."
    };

    internal static readonly Option<float> MinQualityOption = new("--min-quality", "-minq")
    {
        Description = "Minimum QUAL threshold required before a variant is annotated.",
        DefaultValueFactory = _ => 0.0f
    };

    internal static readonly Option<DirectoryInfo> OutputDirOption = new("--output-dir")
    {
        Description = "Output folder.",
        DefaultValueFactory = _ =>
            new DirectoryInfo(Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, "output")))
    };

    internal static readonly Option<string> FastqRequiredOption = new("--fastq", "-fq")
    {
        Description = "FASTQ or FASTQ.GZ file to process.",
        Required = true
    };

    internal static readonly Option<string> AdapterOption = new("--adapter", "-a")
    {
        Description = "Adapter sequence to trim from reads."
    };

    internal static readonly Option<int> MinLengthOption = new("--min-length", "-ml")
    {
        Description = "Minimum read length after trimming. Shorter reads are discarded.",
        DefaultValueFactory = _ => 20
    };

    internal static readonly Option<int> MaxMismatchesOption = new("--max-mismatches", "-mm")
    {
        Description = "Maximum mismatches allowed during adapter matching.",
        DefaultValueFactory = _ => 2
    };

    internal static readonly Option<FileInfo?> BamOption = new("--bam", "-b")
      {
        Description = "Input sorted BAM file for variant calling or alignment (alternative to --fastq/--fasta).",
        Required = false
      };

    internal static readonly Option<string> BamRequiredOption = new("--bam", "-b")
    {
        Description = "Input sorted BAM file.",
        Required = true
    };

    internal static readonly Option<string?> LibraryOption = new("--library", "-lib")
    {
        Description = "Path to a repeat library JSON file (repeats.json). If omitted, no masking is applied."
    };

    internal static readonly Option<int> OpticalPixelDistanceOption = new("--optical-pixel-distance", "-opd")
    {
        Description = "Maximum Euclidean pixel distance within the same tile to call an optical duplicate.",
        DefaultValueFactory = _ => 100
    };

    internal static readonly Option<int?> MinMotifLengthOption = new("--min-motif-length", "-mml")
    {
        Description = "Minimum motif length when loading a repeat library. Shorter motifs are ignored."
    };

    internal static readonly Option<string> FastaRequiredOption = new("--fasta", "-fa")
    {
        Description = "FASTA or FASTA.GZ file to process.",
        Required = true
    };

    internal static readonly Option<string?> BedOption = new("--bed")
    {
        Description = "Optional BED file of target intervals. When provided, coverage statistics are computed only over these regions."
    };

    internal static readonly Option<bool> WriteDepthTsvOption = new("--write-depth-tsv")
    {
        Description = "Write a per-position depth TSV file (equivalent to samtools depth -a output)."
    };

    internal static readonly Option<int> FmSampleRateOption = new("--fm-sample-rate")
    {
        Description = "Sampling rate for the FM-index suffix array (higher = smaller index, slower locate).",
        DefaultValueFactory = _ => 64
    };

    internal static readonly Option<bool> AlsoBuildReferenceIndexOption = new("--also-build-reference-index")
    {
        Description = "Also build and save a k-mer reference index (.bsi) alongside the FM-index. Only valid with --fasta input."
    };

    internal static readonly Option<string?> PreloadIndexOption = new("--index")
    {
        Description = "Path to a pre-built FM-index file (.fmi). When provided the index is loaded instead of rebuilt from the reference."
    };

    // Alignment-specific options
    internal static readonly Option<int> MinSeedLenOption = new("--min-seed-len", "-ms")
     {
        Description = "Minimum seed length for FM-index seeding (like BWA-MEM).",
        DefaultValueFactory = _ => 19
     };

    internal static readonly Option<double> MaxSeedHitsThresholdOption = new("--max-seed-hits")
     {
        Description = "Discard seeds that map to more than this many positions in the reference.",
        DefaultValueFactory = _ => 64.0
     };

    internal static readonly Option<int> SeedStepOption = new("--seed-step", "-ss")
     {
        Description = "Step size between sampled seeds in the read (1 = check every position).",
        DefaultValueFactory = _ => 1
     };

    internal static readonly Option<int> WindowPaddingOption = new("--window-padding", "-wp")
     {
        Description = "Extra bases included on both sides of a candidate window.",
        DefaultValueFactory = _ => 64
     };

    internal static readonly Option<int> MaxCandidateWindowsPerReadOption = new("--max-windows")
     {
        Description = "Maximum candidate windows returned per read before SMW extension.",
        DefaultValueFactory = _ => 8
     };
}
