using OpenMedStack.BioSharp.AnnotationDb;

namespace OpenMedStack.BioSharp.Importer;

class Program
{
    private static async Task Main()
    {
        await using var context = new TranscriptAnnotationDbContext("Data Source=data/transcripts.db");
        var database = new TranscriptAnnotationDatabase(context);
        Console.WriteLine("Initializing database...");
        await database.Initialize();
        var amount = 0;

        // ── Ensembl (optional – skip gracefully when data files are not present) ──
        const string ensemblGtf = "data/ensembl/Homo_sapiens.GRCh38.115.gtf";
        const string ensemblFasta = "data/ensembl/Homo_sapiens.GRCh38.cdna.all.fa";
        if (File.Exists(ensemblGtf) && !File.Exists(ensemblFasta))
        {
            Console.WriteLine("Importing Ensembl data...");
            var result = await database.Import(
                new EnsemblTranscriptDatabaseImporter(),
                new TranscriptImportRequest(
                    AnnotationPath: ensemblGtf,
                    SequencePath: ensemblFasta,
                    Assembly: "GRCh38",
                    SourceVersion: "115"));
            amount += result.TranscriptCount;
            Console.WriteLine($"Imported {result.TranscriptCount} transcripts from {result.SourceName}.");
        }
        else
        {
            Console.WriteLine("Ensembl data files not found – skipping Ensembl import.");
            Console.WriteLine($"  Expected: {ensemblGtf}  and  {ensemblFasta}");
        }

        // ── GENCODE ────────────────────────────────────────────────────────────────
        const string gencodeGtf = "data/gencode/gencode.v48.annotation.gtf";
        const string gencodeFasta = "data/gencode/gencode.v48.transcripts.fa";
        if (File.Exists(gencodeGtf) && !File.Exists(gencodeFasta))
        {
            Console.WriteLine("Importing Gencode data...");
            var result2 = await database.Import(
                new GencodeTranscriptDatabaseImporter(),
                new TranscriptImportRequest(
                    AnnotationPath: gencodeGtf,
                    SequencePath: gencodeFasta,
                    Assembly: "GRCh38",
                    SourceVersion: "v48"));
            Console.WriteLine($"Imported {result2.TranscriptCount} transcripts from {result2.SourceName}.");
            amount += result2.TranscriptCount;
        }
        else
        {
            Console.WriteLine("GENCODE data files not found – skipping GENCODE import.");
            Console.WriteLine($"  Expected: {gencodeGtf}  and  {gencodeFasta}");
        }

        // ── RefSeq ──────────────────────────────────────────────────────────────────
        // NOTE: Use the *_rna.fna file (individual transcript/mRNA sequences).
        //       The *_genomic.fna file contains full chromosome sequences keyed by
        //       NC_* accessions which do not match the NM_*/NR_* transcript IDs in
        //       the GFF3, so it will never produce any matches.
        const string refSeqGff = "data/refseq/GCF_000001405.40_GRCh38.p14_genomic.gff";
        const string refSeqRnaFasta = "data/refseq/GCF_000001405.40_GRCh38.p14_genomic.fna";
        if (File.Exists(refSeqGff) && File.Exists(refSeqRnaFasta))
        {
            Console.WriteLine("Importing RefSeq data...");
            var result3 = await database.Import(
                new RefSeqTranscriptDatabaseImporter(),
                new TranscriptImportRequest(
                    AnnotationPath: refSeqGff,
                    SequencePath: refSeqRnaFasta,
                    Assembly: "GRCh38",
                    SourceVersion: "GCF_000001405.40_GRCh38.p14_genomic"));
            Console.WriteLine($"Imported {result3.TranscriptCount} transcripts from {result3.SourceName}.");
            amount += result3.TranscriptCount;
        }
        else
        {
            Console.WriteLine("RefSeq data files not found – skipping RefSeq import.");
            Console.WriteLine($"  Expected: {refSeqGff}  and  {refSeqRnaFasta}");
            Console.WriteLine("  Download the RNA FASTA from: https://ftp.ncbi.nlm.nih.gov/genomes/all/GCF/000/001/405/GCF_000001405.40_GRCh38.p14/GCF_000001405.40_GRCh38.p14_rna.fna.gz");
        }

        Console.WriteLine($"Imported {amount} transcripts in total.");
    }
}
