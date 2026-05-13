#nullable disable

using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace OpenMedStack.BioSharp.AnnotationDb.Migrations;

[DbContext(typeof(TranscriptAnnotationDbContext))]
[Migration("20260519230000_InitialCreate")]
public partial class InitialCreate : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "transcript_sources",
            columns: table => new
            {
                SourceId = table.Column<string>(type: "TEXT", nullable: false),
                SourceName = table.Column<string>(type: "TEXT", nullable: false),
                Assembly = table.Column<string>(type: "TEXT", nullable: false),
                SourceVersion = table.Column<string>(type: "TEXT", nullable: false),
                AnnotationPath = table.Column<string>(type: "TEXT", nullable: false),
                SequencePath = table.Column<string>(type: "TEXT", nullable: false),
                ImportedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_transcript_sources", x => x.SourceId);
            });

        migrationBuilder.CreateTable(
            name: "transcripts",
            columns: table => new
            {
                TranscriptId = table.Column<string>(type: "TEXT", nullable: false),
                SourceId = table.Column<string>(type: "TEXT", nullable: false),
                GeneId = table.Column<string>(type: "TEXT", nullable: true),
                GeneName = table.Column<string>(type: "TEXT", nullable: true),
                Chromosome = table.Column<string>(type: "TEXT", nullable: false),
                Strand = table.Column<string>(type: "TEXT", nullable: false),
                GeneStart = table.Column<int>(type: "INTEGER", nullable: false),
                GeneEnd = table.Column<int>(type: "INTEGER", nullable: false),
                CdsStart = table.Column<int>(type: "INTEGER", nullable: false),
                CdsEnd = table.Column<int>(type: "INTEGER", nullable: false),
                TranscriptLength = table.Column<int>(type: "INTEGER", nullable: false),
                IsCanonical = table.Column<bool>(type: "INTEGER", nullable: false),
                Sequence = table.Column<string>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_transcripts", x => x.TranscriptId);
                table.ForeignKey(
                    name: "FK_transcripts_transcript_sources_SourceId",
                    column: x => x.SourceId,
                    principalTable: "transcript_sources",
                    principalColumn: "SourceId",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "transcript_exons",
            columns: table => new
            {
                TranscriptId = table.Column<string>(type: "TEXT", nullable: false),
                ExonIndex = table.Column<int>(type: "INTEGER", nullable: false),
                ExonStart = table.Column<int>(type: "INTEGER", nullable: false),
                ExonEnd = table.Column<int>(type: "INTEGER", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_transcript_exons", x => new { x.TranscriptId, x.ExonIndex });
                table.ForeignKey(
                    name: "FK_transcript_exons_transcripts_TranscriptId",
                    column: x => x.TranscriptId,
                    principalTable: "transcripts",
                    principalColumn: "TranscriptId",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "transcript_introns",
            columns: table => new
            {
                TranscriptId = table.Column<string>(type: "TEXT", nullable: false),
                IntronIndex = table.Column<int>(type: "INTEGER", nullable: false),
                IntronStart = table.Column<int>(type: "INTEGER", nullable: false),
                IntronEnd = table.Column<int>(type: "INTEGER", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_transcript_introns", x => new { x.TranscriptId, x.IntronIndex });
                table.ForeignKey(
                    name: "FK_transcript_introns_transcripts_TranscriptId",
                    column: x => x.TranscriptId,
                    principalTable: "transcripts",
                    principalColumn: "TranscriptId",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "idx_transcript_exons_transcript",
            table: "transcript_exons",
            columns: new[] { "TranscriptId", "ExonIndex" });

        migrationBuilder.CreateIndex(
            name: "idx_transcript_introns_transcript",
            table: "transcript_introns",
            columns: new[] { "TranscriptId", "IntronIndex" });

        migrationBuilder.CreateIndex(
            name: "idx_transcripts_canonical",
            table: "transcripts",
            columns: new[] { "IsCanonical", "TranscriptId" });

        migrationBuilder.CreateIndex(
            name: "idx_transcripts_lookup",
            table: "transcripts",
            columns: new[] { "Chromosome", "GeneStart", "GeneEnd" });

        migrationBuilder.CreateIndex(
            name: "idx_transcripts_source_id",
            table: "transcripts",
            column: "SourceId");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "transcript_exons");

        migrationBuilder.DropTable(
            name: "transcript_introns");

        migrationBuilder.DropTable(
            name: "transcripts");

        migrationBuilder.DropTable(
            name: "transcript_sources");
    }
}
