#nullable disable

using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace OpenMedStack.BioSharp.AnnotationDb.Migrations;

[DbContext(typeof(TranscriptAnnotationDbContext))]
public partial class TranscriptAnnotationDbContextModelSnapshot : ModelSnapshot
{
    protected override void BuildModel(ModelBuilder modelBuilder)
    {
        modelBuilder.HasAnnotation("ProductVersion", "9.0.10");

        modelBuilder.Entity("OpenMedStack.BioSharp.AnnotationDb.TranscriptEntity", entity =>
        {
            entity.Property<string>("TranscriptId")
                .HasColumnType("TEXT");

            entity.Property<int>("CdsEnd")
                .HasColumnType("INTEGER");

            entity.Property<int>("CdsStart")
                .HasColumnType("INTEGER");

            entity.Property<string>("Chromosome")
                .IsRequired()
                .HasColumnType("TEXT");

            entity.Property<string>("GeneId")
                .HasColumnType("TEXT");

            entity.Property<string>("GeneName")
                .HasColumnType("TEXT");

            entity.Property<int>("GeneEnd")
                .HasColumnType("INTEGER");

            entity.Property<int>("GeneStart")
                .HasColumnType("INTEGER");

            entity.Property<bool>("IsCanonical")
                .HasColumnType("INTEGER");

            entity.Property<string>("Sequence")
                .IsRequired()
                .HasColumnType("TEXT");

            entity.Property<string>("SourceId")
                .IsRequired()
                .HasColumnType("TEXT");

            entity.Property<string>("Strand")
                .IsRequired()
                .HasColumnType("TEXT");

            entity.Property<int>("TranscriptLength")
                .HasColumnType("INTEGER");

            entity.HasKey("TranscriptId");

            entity.HasIndex("IsCanonical", "TranscriptId")
                .HasDatabaseName("idx_transcripts_canonical");

            entity.HasIndex("Chromosome", "GeneStart", "GeneEnd")
                .HasDatabaseName("idx_transcripts_lookup");

            entity.HasIndex("SourceId")
                .HasDatabaseName("idx_transcripts_source_id");

            entity.ToTable("transcripts");
        });

        modelBuilder.Entity("OpenMedStack.BioSharp.AnnotationDb.TranscriptExonEntity", entity =>
        {
            entity.Property<string>("TranscriptId")
                .HasColumnType("TEXT");

            entity.Property<int>("ExonIndex")
                .HasColumnType("INTEGER");

            entity.Property<int>("ExonEnd")
                .HasColumnType("INTEGER");

            entity.Property<int>("ExonStart")
                .HasColumnType("INTEGER");

            entity.HasKey("TranscriptId", "ExonIndex");

            entity.HasIndex("TranscriptId", "ExonIndex")
                .HasDatabaseName("idx_transcript_exons_transcript");

            entity.ToTable("transcript_exons");
        });

        modelBuilder.Entity("OpenMedStack.BioSharp.AnnotationDb.TranscriptIntronEntity", entity =>
        {
            entity.Property<string>("TranscriptId")
                .HasColumnType("TEXT");

            entity.Property<int>("IntronIndex")
                .HasColumnType("INTEGER");

            entity.Property<int>("IntronEnd")
                .HasColumnType("INTEGER");

            entity.Property<int>("IntronStart")
                .HasColumnType("INTEGER");

            entity.HasKey("TranscriptId", "IntronIndex");

            entity.HasIndex("TranscriptId", "IntronIndex")
                .HasDatabaseName("idx_transcript_introns_transcript");

            entity.ToTable("transcript_introns");
        });

        modelBuilder.Entity("OpenMedStack.BioSharp.AnnotationDb.TranscriptSourceEntity", entity =>
        {
            entity.Property<string>("SourceId")
                .HasColumnType("TEXT");

            entity.Property<string>("AnnotationPath")
                .IsRequired()
                .HasColumnType("TEXT");

            entity.Property<string>("Assembly")
                .IsRequired()
                .HasColumnType("TEXT");

            entity.Property<DateTimeOffset>("ImportedAtUtc")
                .HasColumnType("TEXT");

            entity.Property<string>("SequencePath")
                .IsRequired()
                .HasColumnType("TEXT");

            entity.Property<string>("SourceName")
                .IsRequired()
                .HasColumnType("TEXT");

            entity.Property<string>("SourceVersion")
                .IsRequired()
                .HasColumnType("TEXT");

            entity.HasKey("SourceId");

            entity.ToTable("transcript_sources");
        });

        modelBuilder.Entity("OpenMedStack.BioSharp.AnnotationDb.TranscriptEntity", entity =>
        {
            entity.HasOne("OpenMedStack.BioSharp.AnnotationDb.TranscriptSourceEntity", "Source")
                .WithMany("Transcripts")
                .HasForeignKey("SourceId")
                .OnDelete(DeleteBehavior.Cascade)
                .IsRequired();

            entity.Navigation("Source");
        });

        modelBuilder.Entity("OpenMedStack.BioSharp.AnnotationDb.TranscriptExonEntity", entity =>
        {
            entity.HasOne("OpenMedStack.BioSharp.AnnotationDb.TranscriptEntity", "Transcript")
                .WithMany("Exons")
                .HasForeignKey("TranscriptId")
                .OnDelete(DeleteBehavior.Cascade)
                .IsRequired();

            entity.Navigation("Transcript");
        });

        modelBuilder.Entity("OpenMedStack.BioSharp.AnnotationDb.TranscriptIntronEntity", entity =>
        {
            entity.HasOne("OpenMedStack.BioSharp.AnnotationDb.TranscriptEntity", "Transcript")
                .WithMany("Introns")
                .HasForeignKey("TranscriptId")
                .OnDelete(DeleteBehavior.Cascade)
                .IsRequired();

            entity.Navigation("Transcript");
        });

        modelBuilder.Entity("OpenMedStack.BioSharp.AnnotationDb.TranscriptEntity", entity =>
        {
            entity.Navigation("Exons");

            entity.Navigation("Introns");
        });

        modelBuilder.Entity("OpenMedStack.BioSharp.AnnotationDb.TranscriptSourceEntity", entity =>
        {
            entity.Navigation("Transcripts");
        });
    }
}
