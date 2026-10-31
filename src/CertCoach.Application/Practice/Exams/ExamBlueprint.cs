namespace CertCoach.Application.Practice.Exams;

/// <summary>A domain's weight and the questions available for it.</summary>
public sealed record DomainPool(Guid DomainId, decimal Weight, IReadOnlyList<Guid> ItemIds);

/// <summary>
/// Picks a mock exam's questions in proportion to the domain weights, as the real exam is built. Each domain gets
/// its share by largest remainder; a domain without enough questions gives its spare places to the others.
/// </summary>
public static class ExamBlueprint
{
    public static IReadOnlyList<Guid> Pick(IReadOnlyList<DomainPool> pools, int count, int seed)
    {
        var quotas = Quotas(pools, Math.Min(count, pools.Sum(p => p.ItemIds.Count)));

        // Shuffled within each domain, then across the exam, so questions from one domain don't come in a block.
        return pools
            .SelectMany(p => p.ItemIds.OrderBy(id => Hash(id, seed)).Take(quotas.GetValueOrDefault(p.DomainId)))
            .OrderBy(id => Hash(id, seed + 1))
            .ToList();
    }

    internal static Dictionary<Guid, int> Quotas(IReadOnlyList<DomainPool> pools, int count)
    {
        var quotas = pools.ToDictionary(p => p.DomainId, _ => 0);
        var open = pools.Where(p => p.ItemIds.Count > 0).ToList();
        var left = count;

        while (left > 0 && open.Count > 0)
        {
            var totalWeight = open.Sum(p => p.Weight is > 0 ? p.Weight : 1);
            var shares = open
                .Select(p => (Pool: p, Share: left * (p.Weight is > 0 ? p.Weight : 1) / totalWeight))
                .Select(x => (x.Pool, Whole: (int)Math.Floor(x.Share), Fraction: x.Share - Math.Floor(x.Share)))
                .ToList();

            // Largest remainder: whole parts first, then one more for the biggest fractions until the count is met.
            var extra = left - shares.Sum(s => s.Whole);
            var bonus = shares.OrderByDescending(s => s.Fraction).ThenByDescending(s => s.Pool.Weight).Take(extra).Select(s => s.Pool.DomainId).ToHashSet();

            foreach (var (pool, whole, _) in shares)
            {
                var room = pool.ItemIds.Count - quotas[pool.DomainId];
                var take = Math.Min(room, whole + (bonus.Contains(pool.DomainId) ? 1 : 0));
                quotas[pool.DomainId] += take;
                left -= take;
            }

            open = open.Where(p => quotas[p.DomainId] < p.ItemIds.Count).ToList();
        }

        return quotas;
    }

    private static uint Hash(Guid id, int seed)
    {
        var hash = 2166136261u ^ (uint)seed;
        foreach (var b in id.ToByteArray())
        {
            hash = (hash ^ b) * 16777619;
        }

        return hash;
    }
}
