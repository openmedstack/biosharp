using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using OpenMedStack.BioSharp.Model;

namespace OpenMedStack.BioSharp.AnnotationDb;

public sealed class TranscriptAnnotationDatabase
{
    public TranscriptAnnotationDatabase(TranscriptAnnotationDbContext context)
    {
        Context = context;
    }

    public TranscriptAnnotationDbContext Context { get; }

    public async Task Initialize(CancellationToken cancellationToken = default)
    {
        var conn = await Context.GetOpenConnectionAsync(cancellationToken).ConfigureAwait(false);

        await ExecuteNonQueryAsync(conn, "PRAGMA foreign_keys = ON;", cancellationToken).ConfigureAwait(false);
        await ExecuteNonQueryAsync(conn, "PRAGMA journal_mode = WAL;", cancellationToken).ConfigureAwait(false);

        await ExecuteNonQueryAsync(conn, """
            CREATE TABLE IF NOT EXISTS transcript_sources (
                SourceId        TEXT NOT NULL PRIMARY KEY,
                SourceName      TEXT NOT NULL,
                Assembly        TEXT NOT NULL,
                SourceVersion   TEXT NOT NULL,
                AnnotationPath  TEXT NOT NULL,
                SequencePath    TEXT NOT NULL,
                ImportedAtUtc   TEXT NOT NULL
            );
            """, cancellationToken).ConfigureAwait(false);

        await ExecuteNonQueryAsync(conn, """
            CREATE TABLE IF NOT EXISTS transcripts (
                TranscriptId      TEXT NOT NULL PRIMARY KEY,
                SourceId          TEXT NOT NULL,
                GeneId            TEXT,
                GeneName          TEXT,
                Chromosome        TEXT NOT NULL,
                Strand            TEXT NOT NULL,
                GeneStart         INTEGER NOT NULL,
                GeneEnd           INTEGER NOT NULL,
                CdsStart          INTEGER NOT NULL,
                CdsEnd            INTEGER NOT NULL,
                TranscriptLength  INTEGER NOT NULL,
                IsCanonical       INTEGER NOT NULL,
                Sequence          TEXT NOT NULL,
                FOREIGN KEY (SourceId) REFERENCES transcript_sources(SourceId) ON DELETE CASCADE
            );
            """, cancellationToken).ConfigureAwait(false);

        await ExecuteNonQueryAsync(conn, """
            CREATE TABLE IF NOT EXISTS transcript_exons (
                TranscriptId  TEXT NOT NULL,
                ExonIndex     INTEGER NOT NULL,
                ExonStart     INTEGER NOT NULL,
                ExonEnd       INTEGER NOT NULL,
                PRIMARY KEY (TranscriptId, ExonIndex),
                FOREIGN KEY (TranscriptId) REFERENCES transcripts(TranscriptId) ON DELETE CASCADE
            );
            """, cancellationToken).ConfigureAwait(false);

        await ExecuteNonQueryAsync(conn, """
            CREATE TABLE IF NOT EXISTS transcript_introns (
                TranscriptId  TEXT NOT NULL,
                IntronIndex   INTEGER NOT NULL,
                IntronStart   INTEGER NOT NULL,
                IntronEnd     INTEGER NOT NULL,
                PRIMARY KEY (TranscriptId, IntronIndex),
                FOREIGN KEY (TranscriptId) REFERENCES transcripts(TranscriptId) ON DELETE CASCADE
            );
            """, cancellationToken).ConfigureAwait(false);

        await ExecuteNonQueryAsync(conn, "CREATE INDEX IF NOT EXISTS idx_transcripts_source_id ON transcripts (SourceId);", cancellationToken).ConfigureAwait(false);
        await ExecuteNonQueryAsync(conn, "CREATE INDEX IF NOT EXISTS idx_transcripts_lookup ON transcripts (Chromosome, GeneStart, GeneEnd);", cancellationToken).ConfigureAwait(false);
        await ExecuteNonQueryAsync(conn, "CREATE INDEX IF NOT EXISTS idx_transcripts_canonical ON transcripts (IsCanonical, TranscriptId);", cancellationToken).ConfigureAwait(false);
        await ExecuteNonQueryAsync(conn, "CREATE INDEX IF NOT EXISTS idx_transcript_exons_transcript ON transcript_exons (TranscriptId, ExonIndex);", cancellationToken).ConfigureAwait(false);
        await ExecuteNonQueryAsync(conn, "CREATE INDEX IF NOT EXISTS idx_transcript_introns_transcript ON transcript_introns (TranscriptId, IntronIndex);", cancellationToken).ConfigureAwait(false);
    }

    public async Task<TranscriptImportResult> Import(
        ITranscriptDatabaseImporter importer,
        TranscriptImportRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(importer);
        ArgumentNullException.ThrowIfNull(request);

        var conn = await Context.GetOpenConnectionAsync(cancellationToken).ConfigureAwait(false);

        var sourceId = Guid.NewGuid().ToString("N");
        var importedAtUtc = DateTimeOffset.UtcNow;
        var transcriptCount = 0;

        using (var insertSourceCmd = conn.CreateCommand())
        {
            insertSourceCmd.CommandText = """
                INSERT INTO transcript_sources (SourceId, SourceName, Assembly, SourceVersion, AnnotationPath, SequencePath, ImportedAtUtc)
                VALUES (@SourceId, @SourceName, @Assembly, @SourceVersion, @AnnotationPath, @SequencePath, @ImportedAtUtc);
                """;
            insertSourceCmd.Parameters.AddWithValue("@SourceId", sourceId);
            insertSourceCmd.Parameters.AddWithValue("@SourceName", importer.SourceName);
            insertSourceCmd.Parameters.AddWithValue("@Assembly", request.Assembly);
            insertSourceCmd.Parameters.AddWithValue("@SourceVersion", request.SourceVersion);
            insertSourceCmd.Parameters.AddWithValue("@AnnotationPath", request.AnnotationPath);
            insertSourceCmd.Parameters.AddWithValue("@SequencePath", request.SequencePath);
            insertSourceCmd.Parameters.AddWithValue("@ImportedAtUtc", importedAtUtc.ToString("O"));
            await insertSourceCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        var existingIds = new HashSet<string>(StringComparer.Ordinal);
        using (var selectIdsCmd = conn.CreateCommand())
        {
            selectIdsCmd.CommandText = "SELECT TranscriptId FROM transcripts;";
            await using var reader = await selectIdsCmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                existingIds.Add(reader.GetString(0));
            }
        }

        await using var transaction = await conn.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await foreach (var transcript in importer.Import(request, cancellationToken).ConfigureAwait(false))
        {
            await UpsertTranscript(conn, transaction, sourceId, transcript, existingIds, cancellationToken).ConfigureAwait(false);
            transcriptCount++;
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        return new TranscriptImportResult(importer.SourceName, sourceId, transcriptCount);
    }

    public async Task<StoredTranscript?> GetTranscript(
        string transcriptId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(transcriptId);

        var conn = await Context.GetOpenConnectionAsync(cancellationToken).ConfigureAwait(false);

        TranscriptEntity? entity;
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT TranscriptId, SourceId, GeneId, GeneName, Chromosome, Strand, GeneStart, GeneEnd, CdsStart, CdsEnd, TranscriptLength, IsCanonical, Sequence FROM transcripts WHERE TranscriptId = @Id;";
            cmd.Parameters.AddWithValue("@Id", transcriptId);
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            entity = await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? ReadTranscriptEntity(reader) : null;
        }

        if (entity == null)
        {
            return null;
        }

        await LoadExonsAndIntronsAsync(conn, [entity], cancellationToken).ConfigureAwait(false);
        return ToStoredTranscript(entity);
    }

    public async Task<IReadOnlyList<StoredTranscript>> FindTranscriptsForVariant(
        string chromosome,
        int position,
        string? transcriptId = null,
        CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(transcriptId))
        {
            var single = await GetTranscript(transcriptId, cancellationToken).ConfigureAwait(false);
            return single == null ? [] : [single];
        }

        var conn = await Context.GetOpenConnectionAsync(cancellationToken).ConfigureAwait(false);

        var entities = new List<TranscriptEntity>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                SELECT TranscriptId, SourceId, GeneId, GeneName, Chromosome, Strand, GeneStart, GeneEnd, CdsStart, CdsEnd, TranscriptLength, IsCanonical, Sequence
                FROM transcripts
                WHERE Chromosome = @Chr AND GeneStart <= @Pos AND GeneEnd >= @Pos
                ORDER BY IsCanonical DESC, TranscriptId ASC;
                """;
            cmd.Parameters.AddWithValue("@Chr", chromosome);
            cmd.Parameters.AddWithValue("@Pos", position);
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                entities.Add(ReadTranscriptEntity(reader));
            }
        }

        if (entities.Count == 0)
        {
            return [];
        }

        await LoadExonsAndIntronsAsync(conn, entities, cancellationToken).ConfigureAwait(false);
        return entities.Select(ToStoredTranscript).ToArray();
    }

    private static async Task LoadExonsAndIntronsAsync(
        SqliteConnection conn,
        IReadOnlyList<TranscriptEntity> entities,
        CancellationToken cancellationToken)
    {
        if (entities.Count == 0)
        {
            return;
        }

        var placeholders = string.Join(", ", entities.Select((_, i) => $"@id{i}"));

        var exonsBySid = new Dictionary<string, List<TranscriptExonEntity>>(StringComparer.Ordinal);
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = $"SELECT TranscriptId, ExonIndex, ExonStart, ExonEnd FROM transcript_exons WHERE TranscriptId IN ({placeholders}) ORDER BY TranscriptId, ExonIndex;";
            for (var i = 0; i < entities.Count; i++)
            {
                cmd.Parameters.AddWithValue($"@id{i}", entities[i].TranscriptId);
            }

            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var tid = reader.GetString(0);
                if (!exonsBySid.TryGetValue(tid, out var list))
                {
                    list = [];
                    exonsBySid[tid] = list;
                }

                list.Add(new TranscriptExonEntity
                {
                    TranscriptId = tid,
                    ExonIndex = reader.GetInt32(1),
                    ExonStart = reader.GetInt32(2),
                    ExonEnd = reader.GetInt32(3)
                });
            }
        }

        var intronsBySid = new Dictionary<string, List<TranscriptIntronEntity>>(StringComparer.Ordinal);
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = $"SELECT TranscriptId, IntronIndex, IntronStart, IntronEnd FROM transcript_introns WHERE TranscriptId IN ({placeholders}) ORDER BY TranscriptId, IntronIndex;";
            for (var i = 0; i < entities.Count; i++)
            {
                cmd.Parameters.AddWithValue($"@id{i}", entities[i].TranscriptId);
            }

            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var tid = reader.GetString(0);
                if (!intronsBySid.TryGetValue(tid, out var list))
                {
                    list = [];
                    intronsBySid[tid] = list;
                }

                list.Add(new TranscriptIntronEntity
                {
                    TranscriptId = tid,
                    IntronIndex = reader.GetInt32(1),
                    IntronStart = reader.GetInt32(2),
                    IntronEnd = reader.GetInt32(3)
                });
            }
        }

        foreach (var entity in entities)
        {
            entity.Exons = exonsBySid.GetValueOrDefault(entity.TranscriptId, []);
            entity.Introns = intronsBySid.GetValueOrDefault(entity.TranscriptId, []);
        }
    }

    private static async Task UpsertTranscript(
        SqliteConnection conn,
        IDbTransaction transaction,
        string sourceId,
        StoredTranscript transcript,
        ISet<string> existingIds,
        CancellationToken cancellationToken)
    {
        var geneStart = transcript.Context.GeneBoundaries?.Start ?? transcript.Context.CdsStart;
        var geneEnd = transcript.Context.GeneBoundaries?.End ?? transcript.Context.CdsEnd;

        if (!existingIds.Contains(transcript.TranscriptId))
        {
            using var insertCmd = conn.CreateCommand();
            insertCmd.Transaction = (SqliteTransaction)transaction;
            insertCmd.CommandText = """
                INSERT INTO transcripts (TranscriptId, SourceId, GeneId, GeneName, Chromosome, Strand, GeneStart, GeneEnd, CdsStart, CdsEnd, TranscriptLength, IsCanonical, Sequence)
                VALUES (@TranscriptId, @SourceId, @GeneId, @GeneName, @Chromosome, @Strand, @GeneStart, @GeneEnd, @CdsStart, @CdsEnd, @TranscriptLength, @IsCanonical, @Sequence);
                """;
            AddTranscriptParameters(insertCmd, transcript, sourceId, geneStart, geneEnd);
            await insertCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            existingIds.Add(transcript.TranscriptId);
        }
        else
        {
            using var updateCmd = conn.CreateCommand();
            updateCmd.Transaction = (SqliteTransaction)transaction;
            updateCmd.CommandText = """
                UPDATE transcripts SET SourceId = @SourceId, GeneId = @GeneId, GeneName = @GeneName, Chromosome = @Chromosome,
                    Strand = @Strand, GeneStart = @GeneStart, GeneEnd = @GeneEnd, CdsStart = @CdsStart, CdsEnd = @CdsEnd,
                    TranscriptLength = @TranscriptLength, IsCanonical = @IsCanonical, Sequence = @Sequence
                WHERE TranscriptId = @TranscriptId;
                """;
            AddTranscriptParameters(updateCmd, transcript, sourceId, geneStart, geneEnd);
            await updateCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

            using var delExonsCmd = conn.CreateCommand();
            delExonsCmd.Transaction = (SqliteTransaction)transaction;
            delExonsCmd.CommandText = "DELETE FROM transcript_exons WHERE TranscriptId = @Id;";
            delExonsCmd.Parameters.AddWithValue("@Id", transcript.TranscriptId);
            await delExonsCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

            using var delIntronsCmd = conn.CreateCommand();
            delIntronsCmd.Transaction = (SqliteTransaction)transaction;
            delIntronsCmd.CommandText = "DELETE FROM transcript_introns WHERE TranscriptId = @Id;";
            delIntronsCmd.Parameters.AddWithValue("@Id", transcript.TranscriptId);
            await delIntronsCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await InsertExonsAsync(conn, transaction, transcript, cancellationToken).ConfigureAwait(false);
        await InsertIntronsAsync(conn, transaction, transcript, cancellationToken).ConfigureAwait(false);
    }

    private static void AddTranscriptParameters(
        SqliteCommand cmd,
        StoredTranscript transcript,
        string sourceId,
        int geneStart,
        int geneEnd)
    {
        cmd.Parameters.AddWithValue("@TranscriptId", transcript.TranscriptId);
        cmd.Parameters.AddWithValue("@SourceId", sourceId);
        cmd.Parameters.AddWithValue("@GeneId", (object?)transcript.GeneId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@GeneName", (object?)transcript.GeneName ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Chromosome", transcript.Chromosome);
        cmd.Parameters.AddWithValue("@Strand", transcript.Strand.ToString());
        cmd.Parameters.AddWithValue("@GeneStart", geneStart);
        cmd.Parameters.AddWithValue("@GeneEnd", geneEnd);
        cmd.Parameters.AddWithValue("@CdsStart", transcript.Context.CdsStart);
        cmd.Parameters.AddWithValue("@CdsEnd", transcript.Context.CdsEnd);
        cmd.Parameters.AddWithValue("@TranscriptLength", transcript.Context.TranscriptLength);
        cmd.Parameters.AddWithValue("@IsCanonical", transcript.IsCanonical ? 1 : 0);
        cmd.Parameters.AddWithValue("@Sequence", transcript.Sequence);
    }

    private static async Task InsertExonsAsync(
        SqliteConnection conn,
        IDbTransaction transaction,
        StoredTranscript transcript,
        CancellationToken cancellationToken)
    {
        for (var i = 0; i < transcript.Exons.Count; i++)
        {
            var exon = transcript.Exons[i];
            using var cmd = conn.CreateCommand();
            cmd.Transaction = (SqliteTransaction)transaction;
            cmd.CommandText = "INSERT INTO transcript_exons (TranscriptId, ExonIndex, ExonStart, ExonEnd) VALUES (@TranscriptId, @ExonIndex, @ExonStart, @ExonEnd);";
            cmd.Parameters.AddWithValue("@TranscriptId", transcript.TranscriptId);
            cmd.Parameters.AddWithValue("@ExonIndex", i);
            cmd.Parameters.AddWithValue("@ExonStart", exon.Start);
            cmd.Parameters.AddWithValue("@ExonEnd", exon.End);
            await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task InsertIntronsAsync(
        SqliteConnection conn,
        IDbTransaction transaction,
        StoredTranscript transcript,
        CancellationToken cancellationToken)
    {
        for (var i = 0; i < transcript.Introns.Count; i++)
        {
            var intron = transcript.Introns[i];
            using var cmd = conn.CreateCommand();
            cmd.Transaction = (SqliteTransaction)transaction;
            cmd.CommandText = "INSERT INTO transcript_introns (TranscriptId, IntronIndex, IntronStart, IntronEnd) VALUES (@TranscriptId, @IntronIndex, @IntronStart, @IntronEnd);";
            cmd.Parameters.AddWithValue("@TranscriptId", transcript.TranscriptId);
            cmd.Parameters.AddWithValue("@IntronIndex", i);
            cmd.Parameters.AddWithValue("@IntronStart", intron.Start);
            cmd.Parameters.AddWithValue("@IntronEnd", intron.End);
            await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static TranscriptEntity ReadTranscriptEntity(SqliteDataReader reader)
    {
        return new TranscriptEntity
        {
            TranscriptId = reader.GetString(0),
            SourceId = reader.GetString(1),
            GeneId = reader.IsDBNull(2) ? null : reader.GetString(2),
            GeneName = reader.IsDBNull(3) ? null : reader.GetString(3),
            Chromosome = reader.GetString(4),
            Strand = reader.GetString(5),
            GeneStart = reader.GetInt32(6),
            GeneEnd = reader.GetInt32(7),
            CdsStart = reader.GetInt32(8),
            CdsEnd = reader.GetInt32(9),
            TranscriptLength = reader.GetInt32(10),
            IsCanonical = reader.GetInt32(11) != 0,
            Sequence = reader.GetString(12)
        };
    }

    private static StoredTranscript ToStoredTranscript(TranscriptEntity entity)
    {
        var exons = entity.Exons
            .Select(exon => (exon.ExonStart, exon.ExonEnd))
            .ToList();
        var introns = entity.Introns
            .Select(intron => (intron.IntronStart, intron.IntronEnd))
            .ToList();

        return new StoredTranscript(
            TranscriptId: entity.TranscriptId,
            GeneId: entity.GeneId,
            GeneName: entity.GeneName,
            Chromosome: entity.Chromosome,
            Strand: string.IsNullOrEmpty(entity.Strand) ? '.' : entity.Strand[0],
            IsCanonical: entity.IsCanonical,
            Sequence: entity.Sequence,
            Context: new AnnotationContext
            {
                CdsStart = entity.CdsStart,
                CdsEnd = entity.CdsEnd,
                TranscriptLength = entity.TranscriptLength,
                GeneBoundaries = (entity.GeneStart, entity.GeneEnd),
                ExonBoundaries = exons,
                Introns = introns
            },
            Exons: exons,
            Introns: introns);
    }

    private static async Task ExecuteNonQueryAsync(SqliteConnection conn, string sql, CancellationToken cancellationToken)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}

