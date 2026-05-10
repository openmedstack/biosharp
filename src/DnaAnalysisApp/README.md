# DnaAnalysisApp

A command-line DNA analysis pipeline that aligns short reads against a reference genome, calls variants (SNPs, indels, structural variants), and writes VCF, TSV, and summary outputs.

## Usage

```
dotnet run --project src/DnaAnalysisApp -- --reference <path> --fastq <path> [options]
```

## Parameters

### Required

| Parameter | Description |
|-----------|-------------|
| `--reference <path>` | FASTA or gzipped FASTA (`.fa`, `.fna`, `.fa.gz`, `.fna.gz`) reference sequence file. |
| `--fastq <path>` | Gzipped FASTQ file (`.fastq.gz`) containing reads to align and analyse. |

### Optional

| Parameter | Default | Description |
|-----------|---------|-------------|
| `--reference-id-contains <text>` | _(first record)_ | Select the FASTA record whose ID contains this substring. Useful when the reference file contains multiple sequences. |
| `--chromosome <name>` | _(derived from FASTA ID)_ | Override the contig/chromosome name written to VCF and report outputs. |
| `--output-dir <path>` | `./output` | Directory where output files are written. Created if it does not exist. |
| `--output-prefix <name>` | `variants` | Filename prefix for all output files (`.vcf`, `.tsv`, `.summary.txt`). |
| `--max-reads <int>` | _(all reads)_ | Stop after processing this many reads. Useful for quick smoke tests. |
| `--min-alignment-score <int>` | `10` | Minimum Smith-Waterman alignment score required to accept an alignment. |
| `--min-variant-quality <int>` | `30` | Minimum base quality threshold for a variant call to be retained. |
| `--disable-softclip-realign` | _(enabled)_ | Disable soft-clip realignment used for structural-variant discovery. |
| `--enable-graph-sv` | _(disabled)_ | Run a full-reference De Bruijn graph analysis for additional SV detection. |
| `--kmer-size <int>` | `15` | K-mer length used when building the De Bruijn graph. |
| `--min-graph-coverage <int>` | `5` | Minimum k-mer coverage required to keep an edge in the graph. |
| `--graph-window-bp <int>` | `500` | Window size (bp) used during graph-based SV detection. |
| `--help`, `-h` | | Print usage information and exit. |

## Outputs

All output files are written to `--output-dir` with the stem `--output-prefix`:

| File | Description |
|------|-------------|
| `<prefix>.vcf` | Standard VCF 4.2 file with merged variant calls. |
| `<prefix>.tsv` | Tab-separated table with one variant per row and extended fields. |
| `<prefix>.summary.txt` | Human-readable run summary (read counts, metrics, per-type variant counts). |

## Example — quick smoke test with sample data

Run from the repository root (`/Users/reimersj/code/dna-analysis`):

```bash
dotnet run --project src/DnaAnalysisApp -- \
  --reference data/GCF_000005845.2_ASM584v2_genomic.fna.gz \
  --fastq data/ERR11270506.one-read.fastq.gz \
  --chromosome NC_000913.3 \
  --output-dir output/ecoli-one-read \
  --output-prefix ERR11270506.one-read \
  --max-reads 1
```

## Example — full E. coli run

```bash
dotnet run --project src/DnaAnalysisApp -- \
  --reference data/GCF_000005845.2_ASM584v2_genomic.fna.gz \
  --fastq data/ERR11270506.fastq.gz \
  --chromosome NC_000913.3 \
  --output-dir output/ecoli-full \
  --output-prefix ERR11270506 \
  --min-alignment-score 10 \
  --min-variant-quality 30
```

## Example — with De Bruijn graph SV detection

```bash
dotnet run --project src/DnaAnalysisApp -- \
  --reference data/GCF_000005845.2_ASM584v2_genomic.fna.gz \
  --fastq data/ERR11270506.fastq.gz \
  --chromosome NC_000913.3 \
  --output-dir output/ecoli-graph \
  --output-prefix ERR11270506.graph \
  --enable-graph-sv \
  --kmer-size 15 \
  --min-graph-coverage 5
```
