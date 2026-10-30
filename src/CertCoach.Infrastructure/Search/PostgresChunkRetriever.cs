using CertCoach.Application.Search;
using CertCoach.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Pgvector;

namespace CertCoach.Infrastructure.Search;

/// <summary>
/// Both retrievers as plain SQL over <c>document_chunks</c>: pgvector's cosine distance (HNSW index) and
/// PostgreSQL full-text search (GIN index). Only chunks of ready documents are searched.
/// Result columns are snake_case because the context's naming convention also applies to <see cref="ChunkHit"/>.
/// </summary>
internal sealed class PostgresChunkRetriever(CertCoachDbContext db) : IChunkRetriever
{
    public async Task<IReadOnlyList<ChunkHit>> VectorSearchAsync(
        Guid ownerId, float[] queryVector, Guid? certificationId, int limit, CancellationToken ct)
    {
        var vector = new Vector(queryVector);
        return await db.Database.SqlQuery<ChunkHit>($"""
            SELECT c.id AS chunk_id, 1 - (c.embedding <=> {vector}) AS score
            FROM document_chunks c
            JOIN documents d ON d.id = c.document_id
            WHERE c.owner_id = {ownerId}
              AND d.status = 'Ready'
              AND ({certificationId}::uuid IS NULL OR d.certification_id IS NULL OR d.certification_id = {certificationId})
            ORDER BY c.embedding <=> {vector}
            LIMIT {limit}
            """).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<ChunkHit>> TextSearchAsync(
        Guid ownerId, string query, Guid? certificationId, int limit, CancellationToken ct)
    {
        // plainto_tsquery ANDs every word, which a full question rarely satisfies. Its output is a list of
        // quoted lexemes joined by &, so swapping & for | gives "any of these words" without parsing user input.
        // ts_rank_cd then ranks chunks that match more of the words, closer together, higher.
        return await db.Database.SqlQuery<ChunkHit>($"""
            WITH q AS (
                SELECT replace(plainto_tsquery('english', {query})::text, '&', '|')::tsquery AS query
            )
            SELECT c.id AS chunk_id, ts_rank_cd(c.search_vector, q.query, 1)::float8 AS score
            FROM document_chunks c
            JOIN documents d ON d.id = c.document_id
            CROSS JOIN q
            WHERE c.owner_id = {ownerId}
              AND d.status = 'Ready'
              AND ({certificationId}::uuid IS NULL OR d.certification_id IS NULL OR d.certification_id = {certificationId})
              AND q.query <> ''::tsquery
              AND c.search_vector @@ q.query
            ORDER BY score DESC, c.id
            LIMIT {limit}
            """).ToListAsync(ct);
    }
}
