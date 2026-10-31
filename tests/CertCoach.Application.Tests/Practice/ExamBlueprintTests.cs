using CertCoach.Application.Practice.Exams;

namespace CertCoach.Application.Tests.Practice;

public class ExamBlueprintTests
{
    private static DomainPool Pool(decimal weight, int items) =>
        new(Guid.NewGuid(), weight, Enumerable.Range(0, items).Select(_ => Guid.NewGuid()).ToList());

    [Fact]
    public void Shares_questions_by_domain_weight()
    {
        DomainPool[] pools = [Pool(22.5m, 50), Pool(32.5m, 50), Pool(22.5m, 50), Pool(17.5m, 50)];

        var quotas = ExamBlueprint.Quotas(pools, 40);

        quotas.Values.Sum().ShouldBe(40);
        pools.Select(p => quotas[p.DomainId]).ShouldBe([10, 14, 9, 7], ignoreOrder: false);
    }

    [Fact]
    public void A_domain_short_of_questions_gives_its_places_to_the_others()
    {
        DomainPool[] pools = [Pool(50, 2), Pool(30, 50), Pool(20, 50), Pool(10, 0)];

        var quotas = ExamBlueprint.Quotas(pools, 20);

        quotas[pools[0].DomainId].ShouldBe(2);
        quotas[pools[3].DomainId].ShouldBe(0);
        quotas.Values.Sum().ShouldBe(20);
        quotas[pools[1].DomainId].ShouldBeGreaterThan(quotas[pools[2].DomainId]);
    }

    [Fact]
    public void Picks_distinct_questions_mixed_across_domains_and_never_more_than_exist()
    {
        DomainPool[] pools = [Pool(60, 30), Pool(40, 30)];

        var picked = ExamBlueprint.Pick(pools, 20, seed: 7);
        var everything = ExamBlueprint.Pick(pools, 100, seed: 7);

        picked.Count.ShouldBe(20);
        picked.Distinct().Count().ShouldBe(20);
        picked.Count(pools[0].ItemIds.Contains).ShouldBe(12);
        picked.Take(10).Any(pools[1].ItemIds.Contains).ShouldBeTrue();
        everything.Count.ShouldBe(60);
        ExamBlueprint.Pick(pools, 20, seed: 7).ShouldBe(picked);
    }
}
