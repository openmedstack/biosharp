using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
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
        if (Context.Database.IsRelational())
        {
            await Context.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        await Context.Database.EnsureCreatedAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<TranscriptImportResult> Import(
        ITranscriptDatabaseImporter importer,
        TranscriptImportRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(importer);
        ArgumentNullException.ThrowIfNull(request);

        var sourceId = Guid.NewGuid().ToString("N");
        var importedAtUtc = DateTimeOffset.UtcNow;
        var transcriptCount = 0;
        const int saveBatchSize = 256;

        var useTransaction = Context.Database.IsRelational();
        var transaction = useTransaction
            ? await Context.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false)
            : null;

        Context.TranscriptSources.Add(new TranscriptSourceEntity
        {
            SourceId = sourceId,
            SourceName = importer.SourceName,
            Assembly = request.Assembly,
            SourceVersion = request.SourceVersion,
            AnnotationPath = request.AnnotationPath,
            SequencePath = request.SequencePath,
            ImportedAtUtc = importedAtUtc
        });
        await Context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var existingIds = new HashSet<string>(
            await Context.Transcripts
                .AsNoTracking()
                .Select(entity => entity.TranscriptId)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false),
            StringComparer.Ordinal);

        var pendingChanges = 0;
        await foreach (var transcript in importer.Import(request, cancellationToken).ConfigureAwait(false))
        {
            await UpsertTranscript(sourceId, transcript, existingIds, cancellationToken).ConfigureAwait(false);
            transcriptCount++;
            pendingChanges++;

            if (pendingChanges < saveBatchSize)
            {
                continue;
            }

            await Context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            pendingChanges = 0;
        }

        if (pendingChanges > 0)
        {
            await Context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        if (transaction != null)
        {
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            await transaction.DisposeAsync().ConfigureAwait(false);
        }

        return new TranscriptImportResult(importer.SourceName, sourceId, transcriptCount);
    }

    public async Task<StoredTranscript?> GetTranscript(
        string transcriptId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(transcriptId);
        var transcriptEntity = await Context.Transcripts
            .AsNoTracking()
            .Include(entity => entity.Exons.OrderBy(exon => exon.ExonIndex))
            .Include(entity => entity.Introns.OrderBy(intron => intron.IntronIndex))
            .SingleOrDefaultAsync(entity => entity.TranscriptId == transcriptId, cancellationToken)
            .ConfigureAwait(false);

        return transcriptEntity == null ? null : ToStoredTranscript(transcriptEntity);
    }

    public async Task<IReadOnlyList<StoredTranscript>> FindTranscriptsForVariant(
        string chromosome,
        int position,
        string? transcriptId = null,
        CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(transcriptId))
        {
            var storedTranscript = await GetTranscript(transcriptId, cancellationToken).ConfigureAwait(false);
            return storedTranscript == null ? [] : [storedTranscript];
        }

        var transcriptEntities = await Context.Transcripts
            .AsNoTracking()
            .Include(entity => entity.Exons.OrderBy(exon => exon.ExonIndex))
            .Include(entity => entity.Introns.OrderBy(intron => intron.IntronIndex))
            .Where(entity => entity.Chromosome == chromosome
                             && entity.GeneStart <= position
                             && entity.GeneEnd >= position)
            .OrderByDescending(entity => entity.IsCanonical)
            .ThenBy(entity => entity.TranscriptId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return transcriptEntities.Select(ToStoredTranscript).ToArray();
    }

    private static char GetSingleCharacter(string value, char fallback)
    {
        return string.IsNullOrEmpty(value) ? fallback : value[0];
    }

    private async Task UpsertTranscript(
        string sourceId,
        StoredTranscript transcript,
        ISet<string> existingIds,
        CancellationToken cancellationToken)
    {
        if (!existingIds.Contains(transcript.TranscriptId))
        {
            Context.Transcripts.Add(ToEntity(sourceId, transcript));
            existingIds.Add(transcript.TranscriptId);
            return;
        }

        var existingEntity = await Context.Transcripts
            .Include(entity => entity.Exons)
            .Include(entity => entity.Introns)
            .SingleAsync(entity => entity.TranscriptId == transcript.TranscriptId, cancellationToken)
            .ConfigureAwait(false);

        existingEntity.SourceId = sourceId;
        existingEntity.GeneId = transcript.GeneId;
        existingEntity.GeneName = transcript.GeneName;
        existingEntity.Chromosome = transcript.Chromosome;
        existingEntity.Strand = transcript.Strand.ToString();
        existingEntity.GeneStart = transcript.Context.GeneBoundaries?.Start ?? transcript.Context.CdsStart;
        existingEntity.GeneEnd = transcript.Context.GeneBoundaries?.End ?? transcript.Context.CdsEnd;
        existingEntity.CdsStart = transcript.Context.CdsStart;
        existingEntity.CdsEnd = transcript.Context.CdsEnd;
        existingEntity.TranscriptLength = transcript.Context.TranscriptLength;
        existingEntity.IsCanonical = transcript.IsCanonical;
        existingEntity.Sequence = transcript.Sequence;

        Context.TranscriptExons.RemoveRange(existingEntity.Exons);
        Context.TranscriptIntrons.RemoveRange(existingEntity.Introns);
        existingEntity.Exons = transcript.Exons
            .Select((boundary, index) => new TranscriptExonEntity
            {
                TranscriptId = transcript.TranscriptId,
                ExonIndex = index,
                ExonStart = boundary.Start,
                ExonEnd = boundary.End
            })
            .ToList();
        existingEntity.Introns = transcript.Introns
            .Select((boundary, index) => new TranscriptIntronEntity
            {
                TranscriptId = transcript.TranscriptId,
                IntronIndex = index,
                IntronStart = boundary.Start,
                IntronEnd = boundary.End
            })
            .ToList();
    }

    private static TranscriptEntity ToEntity(string sourceId, StoredTranscript transcript)
    {
        return new TranscriptEntity
        {
            TranscriptId = transcript.TranscriptId,
            SourceId = sourceId,
            GeneId = transcript.GeneId,
            GeneName = transcript.GeneName,
            Chromosome = transcript.Chromosome,
            Strand = transcript.Strand.ToString(),
            GeneStart = transcript.Context.GeneBoundaries?.Start ?? transcript.Context.CdsStart,
            GeneEnd = transcript.Context.GeneBoundaries?.End ?? transcript.Context.CdsEnd,
            CdsStart = transcript.Context.CdsStart,
            CdsEnd = transcript.Context.CdsEnd,
            TranscriptLength = transcript.Context.TranscriptLength,
            IsCanonical = transcript.IsCanonical,
            Sequence = transcript.Sequence,
            Exons = transcript.Exons
                .Select((boundary, index) => new TranscriptExonEntity
                {
                    TranscriptId = transcript.TranscriptId,
                    ExonIndex = index,
                    ExonStart = boundary.Start,
                    ExonEnd = boundary.End
                })
                .ToList(),
            Introns = transcript.Introns
                .Select((boundary, index) => new TranscriptIntronEntity
                {
                    TranscriptId = transcript.TranscriptId,
                    IntronIndex = index,
                    IntronStart = boundary.Start,
                    IntronEnd = boundary.End
                })
                .ToList()
        };
    }

    private static StoredTranscript ToStoredTranscript(TranscriptEntity entity)
    {
        var exons = entity.Exons
            .OrderBy(exon => exon.ExonIndex)
            .Select(exon => (exon.ExonStart, exon.ExonEnd))
            .ToList();
        var introns = entity.Introns
            .OrderBy(intron => intron.IntronIndex)
            .Select(intron => (intron.IntronStart, intron.IntronEnd))
            .ToList();

        return new StoredTranscript(
            TranscriptId: entity.TranscriptId,
            GeneId: entity.GeneId,
            GeneName: entity.GeneName,
            Chromosome: entity.Chromosome,
            Strand: GetSingleCharacter(entity.Strand, '.'),
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
}
