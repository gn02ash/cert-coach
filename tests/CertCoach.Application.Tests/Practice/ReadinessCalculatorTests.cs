using CertCoach.Application.Practice.Readiness;

namespace CertCoach.Application.Tests.Practice;

public class ReadinessCalculatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 2, 8, 0, 0, TimeSpan.Zero);
    private static readonly Guid[] Build = [Guid.NewGuid(), Guid.NewGuid()];
    private static readonly Guid[] Operate = [Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()];

    private static readonly ReadinessDomain[] Domains =
    [
        new(Guid.NewGuid(), "Build", 60, Build),
        new(Guid.NewGuid(), "Operate", 40, Operate),
    ];

    private static IEnumerable<ReadinessAnswer> Questions(Guid objective, int right, int wrong, double daysAgo = 0) =>
        Enumerable.Repeat(true, right).Concat(Enumerable.Repeat(false, wrong))
            .Select(c => new ReadinessAnswer(objective, c, c, Now.AddDays(-daysAgo)));

    [Fact]
    public void A_domain_needs_ten_question_answers_for_a_score()
    {
        var report = ReadinessCalculator.Compute(Domains, [.. Questions(Build[0], 9, 0)], Now);

        var build = report.Domains[0];
        build.Score.ShouldBeNull();
        build.Answers.ShouldBe(9);
        build.Coverage.ShouldBe(50);
        build.Accuracy.ShouldBe(100);
        report.Overall.ShouldBeNull();
    }

    [Fact]
    public void Readiness_is_coverage_times_accuracy_shrunk_toward_even()
    {
        // Both objectives recalled; 10 of 10 right today: (10 + 2) / (10 + 4) = 85.7% accuracy, not 100%.
        var report = ReadinessCalculator.Compute(Domains, [.. Questions(Build[0], 5, 0), .. Questions(Build[1], 5, 0)], Now);

        report.Domains[0].Score.ShouldBe(86);
        report.Domains[0].Coverage.ShouldBe(100);
    }

    [Fact]
    public void Recent_answers_count_more_than_old_ones()
    {
        // The same 10 right and 10 wrong; whichever is recent decides the score.
        ReadinessAnswer[] improving = [.. Questions(Build[0], 0, 10, daysAgo: 28), .. Questions(Build[1], 10, 0)];
        ReadinessAnswer[] slipping = [.. Questions(Build[0], 10, 0, daysAgo: 28), .. Questions(Build[1], 0, 10)];

        var better = ReadinessCalculator.Compute(Domains, improving, Now).Domains[0];
        var worse = ReadinessCalculator.Compute(Domains, slipping, Now).Domains[0];

        // The plain share is 50% both times, but 28-day-old answers weigh a quarter: (10 + 2) / (12.5 + 4) = 72.7%
        // against (2.5 + 2) / (12.5 + 4) = 27.3%. Each covered one of the two objectives, so both are halved.
        better.Accuracy.ShouldBe(worse.Accuracy);
        better.Score.ShouldBe(36);
        worse.Score.ShouldBe(14);
    }

    [Fact]
    public void Flashcards_count_towards_coverage_but_not_accuracy_and_old_answers_drop_out()
    {
        ReadinessAnswer[] answers =
        [
            .. Questions(Operate[0], 10, 0),
            new(Operate[1], null, true, Now), // A flashcard graded Good.
            new(Operate[2], null, false, Now), // Graded Again: not recalled.
            .. Questions(Operate[3], 50, 0, daysAgo: ReadinessCalculator.WindowDays + 1),
        ];

        var operate = ReadinessCalculator.Compute(Domains, answers, Now).Domains[1];

        operate.ObjectivesCovered.ShouldBe(2);
        operate.Answers.ShouldBe(10);
        operate.Score.ShouldBe(43); // 50% coverage × 85.7%.
    }

    [Fact]
    public void Overall_weights_domains_by_exam_weight_once_all_have_scores()
    {
        ReadinessAnswer[] answers =
        [
            .. Questions(Build[0], 5, 0), .. Questions(Build[1], 5, 0),
            .. Operate.SelectMany(o => Questions(o, 0, 3)), .. Questions(Operate[0], 1, 0),
        ];

        var report = ReadinessCalculator.Compute(Domains, answers, Now);

        var (build, operate) = (report.Domains[0].Score!.Value, report.Domains[1].Score!.Value);
        report.DomainsScored.ShouldBe(2);
        report.Overall.ShouldBe((int)Math.Round((60 * build + 40 * operate) / 100.0, MidpointRounding.AwayFromZero));
    }
}
