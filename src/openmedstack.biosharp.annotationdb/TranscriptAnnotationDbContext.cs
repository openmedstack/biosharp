using Microsoft.EntityFrameworkCore;

namespace OpenMedStack.BioSharp.AnnotationDb;

public sealed class TranscriptAnnotationDbContext : DbContext
{
    public TranscriptAnnotationDbContext(DbContextOptions<TranscriptAnnotationDbContext> options)
        : base(options)
    {
    }

    public DbSet<TranscriptSourceEntity> TranscriptSources => Set<TranscriptSourceEntity>();

    public DbSet<TranscriptEntity> Transcripts => Set<TranscriptEntity>();

    public DbSet<TranscriptExonEntity> TranscriptExons => Set<TranscriptExonEntity>();

    public DbSet<TranscriptIntronEntity> TranscriptIntrons => Set<TranscriptIntronEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var sourceEntity = modelBuilder.Entity<TranscriptSourceEntity>();
        sourceEntity.ToTable("transcript_sources");
        sourceEntity.HasKey(entity => entity.SourceId);
        sourceEntity.Property(entity => entity.SourceId).IsRequired();
        sourceEntity.Property(entity => entity.SourceName).IsRequired();
        sourceEntity.Property(entity => entity.Assembly).IsRequired();
        sourceEntity.Property(entity => entity.SourceVersion).IsRequired();
        sourceEntity.Property(entity => entity.AnnotationPath).IsRequired();
        sourceEntity.Property(entity => entity.SequencePath).IsRequired();
        sourceEntity.Property(entity => entity.ImportedAtUtc).IsRequired();
        sourceEntity.HasMany(entity => entity.Transcripts)
            .WithOne(entity => entity.Source)
            .HasForeignKey(entity => entity.SourceId)
            .OnDelete(DeleteBehavior.Cascade);

        var transcriptEntity = modelBuilder.Entity<TranscriptEntity>();
        transcriptEntity.ToTable("transcripts");
        transcriptEntity.HasKey(entity => entity.TranscriptId);
        transcriptEntity.Property(entity => entity.TranscriptId).IsRequired();
        transcriptEntity.Property(entity => entity.SourceId).IsRequired();
        transcriptEntity.Property(entity => entity.Chromosome).IsRequired();
        transcriptEntity.Property(entity => entity.Strand).IsRequired();
        transcriptEntity.Property(entity => entity.Sequence).IsRequired();
        transcriptEntity.HasIndex(entity => entity.SourceId).HasDatabaseName("idx_transcripts_source_id");
        transcriptEntity.HasIndex(entity => new { entity.Chromosome, entity.GeneStart, entity.GeneEnd })
            .HasDatabaseName("idx_transcripts_lookup");
        transcriptEntity.HasIndex(entity => new { entity.IsCanonical, entity.TranscriptId })
            .HasDatabaseName("idx_transcripts_canonical");
        transcriptEntity.HasMany(entity => entity.Exons)
            .WithOne(entity => entity.Transcript)
            .HasForeignKey(entity => entity.TranscriptId)
            .OnDelete(DeleteBehavior.Cascade);
        transcriptEntity.HasMany(entity => entity.Introns)
            .WithOne(entity => entity.Transcript)
            .HasForeignKey(entity => entity.TranscriptId)
            .OnDelete(DeleteBehavior.Cascade);

        var exonEntity = modelBuilder.Entity<TranscriptExonEntity>();
        exonEntity.ToTable("transcript_exons");
        exonEntity.HasKey(entity => new { entity.TranscriptId, entity.ExonIndex });
        exonEntity.Property(entity => entity.TranscriptId).IsRequired();
        exonEntity.HasIndex(entity => new { entity.TranscriptId, entity.ExonIndex })
            .HasDatabaseName("idx_transcript_exons_transcript");

        var intronEntity = modelBuilder.Entity<TranscriptIntronEntity>();
        intronEntity.ToTable("transcript_introns");
        intronEntity.HasKey(entity => new { entity.TranscriptId, entity.IntronIndex });
        intronEntity.Property(entity => entity.TranscriptId).IsRequired();
        intronEntity.HasIndex(entity => new { entity.TranscriptId, entity.IntronIndex })
            .HasDatabaseName("idx_transcript_introns_transcript");
    }
}
