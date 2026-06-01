using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace OpenMedStack.BioSharp.AnnotationDb;

/// <summary>
/// Lightweight ADO.NET wrapper around a SQLite connection for the transcript annotation schema.
/// Replaces the EF Core DbContext to allow NativeAOT compilation.
/// </summary>
public sealed class TranscriptAnnotationDbContext : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private bool _disposed;

    public TranscriptAnnotationDbContext(string connectionString)
    {
        _connection = new SqliteConnection(connectionString);
    }

    internal async ValueTask<SqliteConnection> GetOpenConnectionAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_connection.State == ConnectionState.Closed)
        {
            await _connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        }

        return _connection;
    }

    public async ValueTask DisposeAsync()
    {
        if (!_disposed)
        {
            _disposed = true;
            await _connection.DisposeAsync().ConfigureAwait(false);
        }
    }
}

