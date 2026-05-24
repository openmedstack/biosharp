# OpenMedStack.BioSharp.AnnotationDb

`openmedstack.biosharp.annotationdb` stores transcript annotations in a relational database through **Entity Framework Core** and annotates variants from the stored transcript set instead of requiring callers to preload transcript FASTA data into memory.

## Supported public sources

The project currently supports imports from:

1. **Ensembl** (`EnsemblTranscriptDatabaseImporter`)
2. **GENCODE** (`GencodeTranscriptDatabaseImporter`)
3. **RefSeq** (`RefSeqTranscriptDatabaseImporter`)

Each import expects:

- an annotation file (`.gtf` for Ensembl/GENCODE, `.gff3` for RefSeq)
- a transcript FASTA file
- an assembly name
- a source version string

## Entity Framework Core model

The project now defines the database model with EF Core:

- `transcript_sources`
- `transcripts`
- `transcript_exons`
- `transcript_introns`

The EF Core entry points live in:

- `TranscriptAnnotationDbContext.cs`
- `TranscriptAnnotationDatabase.cs`
- `Migrations/`

The schema is applied with EF Core migrations instead of handwritten SQL commands.

## Downloading source data

### Ensembl

Example release layout:

- GTF: `https://ftp.ensembl.org/pub/current_gtf/homo_sapiens/`
- transcript FASTA: `https://ftp.ensembl.org/pub/current_fasta/homo_sapiens/cdna/`

Example download:

```bash
mkdir -p data/ensembl
cd data/ensembl

curl -O https://ftp.ensembl.org/pub/current_gtf/homo_sapiens/Homo_sapiens.GRCh38.115.gtf.gz
curl -O https://ftp.ensembl.org/pub/current_fasta/homo_sapiens/cdna/Homo_sapiens.GRCh38.cdna.all.fa.gz

gunzip -k Homo_sapiens.GRCh38.115.gtf.gz
gunzip -k Homo_sapiens.GRCh38.cdna.all.fa.gz
```

Use the decompressed files:

- `Homo_sapiens.GRCh38.114.gtf`
- `Homo_sapiens.GRCh38.cdna.all.fa`

### GENCODE

GENCODE human releases are published from:

- `https://ftp.ebi.ac.uk/pub/databases/gencode/Gencode_human/`

Example download:

```bash
mkdir -p data/gencode
cd data/gencode

curl -O https://ftp.ebi.ac.uk/pub/databases/gencode/Gencode_human/release_48/gencode.v48.annotation.gtf.gz
curl -O https://ftp.ebi.ac.uk/pub/databases/gencode/Gencode_human/release_48/gencode.v48.transcripts.fa.gz

gunzip -k gencode.v48.annotation.gtf.gz
gunzip -k gencode.v48.transcripts.fa.gz
```

Use the decompressed files:

- `gencode.v48.annotation.gtf`
- `gencode.v48.transcripts.fa`

### RefSeq

RefSeq human annotation data is published by NCBI from the genomes FTP site. One common source is:

- `https://ftp.ncbi.nlm.nih.gov/genomes/refseq/vertebrate_mammalian/Homo_sapiens/latest_assembly_versions/`

Choose the current assembly directory, then download:

- the genomic annotation GFF3 (`*_genomic.gff.gz`)
- the **RNA** FASTA (`*_rna.fna.gz`) — this file contains individual transcript sequences
  whose IDs (`NM_*`, `NR_*`, etc.) match the `rna-*` identifiers in the GFF3.
  Do **not** use `*_genomic.fna.gz`; that file contains full chromosome sequences
  (keyed `NC_*`) which do not match any transcript ID.

Example:

```bash
mkdir -p data/refseq
cd data/refseq

curl -O https://ftp.ncbi.nlm.nih.gov/genomes/refseq/vertebrate_mammalian/Homo_sapiens/latest_assembly_versions/GCF_000001405.40_GRCh38.p14/GCF_000001405.40_GRCh38.p14_genomic.gff.gz
curl -O https://ftp.ncbi.nlm.nih.gov/genomes/refseq/vertebrate_mammalian/Homo_sapiens/latest_assembly_versions/GCF_000001405.40_GRCh38.p14/GCF_000001405.40_GRCh38.p14_rna.fna.gz

gunzip -k GCF_000001405.40_GRCh38.p14_genomic.gff.gz
gunzip -k GCF_000001405.40_GRCh38.p14_rna.fna.gz
```

Use the decompressed files:

- `GCF_000001405.40_GRCh38.p14_genomic.gff`
- `GCF_000001405.40_GRCh38.p14_rna.fna`

## Configuring the database provider

`TranscriptAnnotationDbContext` accepts normal `DbContextOptions`, so the calling application chooses the provider.

### SQLite example

```csharp
using Microsoft.EntityFrameworkCore;
using OpenMedStack.BioSharp.AnnotationDb;

var options = new DbContextOptionsBuilder<TranscriptAnnotationDbContext>()
    .UseSqlite("Data Source=transcripts.db")
    .Options;

await using var context = new TranscriptAnnotationDbContext(options);
var database = new TranscriptAnnotationDatabase(context);
```

### Other relational providers

The project no longer hardcodes SQLite in the runtime API. A consuming app can configure another EF Core provider instead, for example:

```csharp
var options = new DbContextOptionsBuilder<TranscriptAnnotationDbContext>()
    .UseNpgsql(connectionString)   // or UseSqlServer(...), UseMySql(...), etc.
    .Options;
```

## Applying migrations

`TranscriptAnnotationDatabase.Initialize()` calls `Database.Migrate()`, so it applies pending migrations automatically for the configured provider.

```csharp
await database.Initialize();
```

This project includes an initial migration in `Migrations/`.

If you want to scaffold additional migrations with `dotnet ef`, do that from the application or migration project that references `TranscriptAnnotationDbContext` and has the provider/design packages needed for your chosen database.

## Importing data

The project does not currently ship a CLI importer. Import is done programmatically through `TranscriptAnnotationDatabase`.

### Basic import flow

```csharp
using Microsoft.EntityFrameworkCore;
using OpenMedStack.BioSharp.AnnotationDb;

var options = new DbContextOptionsBuilder<TranscriptAnnotationDbContext>()
    .UseSqlite("Data Source=transcripts.db")
    .Options;

await using var context = new TranscriptAnnotationDbContext(options);
var database = new TranscriptAnnotationDatabase(context);

await database.Initialize(cancellationToken);

var result = await database.Import(
    new EnsemblTranscriptDatabaseImporter(),
    new TranscriptImportRequest(
        AnnotationPath: "data/ensembl/Homo_sapiens.GRCh38.114.gtf",
        SequencePath: "data/ensembl/Homo_sapiens.GRCh38.cdna.all.fa",
        Assembly: "GRCh38",
        SourceVersion: "114"));

Console.WriteLine($"Imported {result.TranscriptCount} transcripts from {result.SourceName}.");
```

### GENCODE import

```csharp
var result = await database.Import(
    new GencodeTranscriptDatabaseImporter(),
    new TranscriptImportRequest(
        AnnotationPath: "data/gencode/gencode.v48.annotation.gtf",
        SequencePath: "data/gencode/gencode.v48.transcripts.fa",
        Assembly: "GRCh38",
        SourceVersion: "v48"));
```

### RefSeq import

```csharp
var result = await database.Import(
    new RefSeqTranscriptDatabaseImporter(),
    new TranscriptImportRequest(
        AnnotationPath: "data/refseq/GCF_000001405.40_GRCh38.p14_genomic.gff",
        SequencePath: "data/refseq/GCF_000001405.40_GRCh38.p14_rna.fna",
        Assembly: "GRCh38",
        SourceVersion: "GCF_000001405.40_GRCh38.p14_genomic"));
```

## Annotating from the database

After import, use `DatabaseVariantAnnotationEngine`:

```csharp
using OpenMedStack.BioSharp.Model.Vcf;

var engine = new DatabaseVariantAnnotationEngine(database);

var annotations = await engine.AnnotateVariant(
    new VcfVariant
    {
        Chromosome = "chr1",
        Position = 4,
        Reference = "G",
        Alternate = "A",
        ErrorProbabilities = [30],
        FailedFilter = []
    },
    transcriptId: "ENST000001");
```

To annotate a VCF file:

```csharp
await foreach (var annotation in engine.AnnotateVcf("variants.vcf"))
{
    Console.WriteLine($"{annotation.AffectedGene}: {annotation.HgvsCoding} {annotation.Consequence}");
}
```

## Notes

1. Importers expect plain-text annotation and FASTA files. If you download `.gz` archives, decompress them before import.
2. Ensembl and GENCODE imports use transcript ids from GTF `transcript_id` attributes and FASTA headers.
3. RefSeq imports normalize `rna-<id>` parent identifiers from GFF3 to the matching FASTA transcript id.
4. `TranscriptAnnotationDatabase` is provider-agnostic at runtime; the caller decides which EF Core provider to use through `DbContextOptions`.
5. The database-backed engine reuses BioSharp's existing variant consequence logic after loading the matching stored transcript rows.
