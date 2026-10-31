namespace CertCoach.Application.Practice.Readiness;

/// <summary>A domain of the outline, with its objectives.</summary>
public sealed record ReadinessDomain(Guid Id, string Name, decimal Weight, IReadOnlyList<Guid> ObjectiveIds);

/// <summary>One logged answer, reduced to what readiness needs.</summary>
/// <param name="Correct">For questions; null for flashcards.</param>
/// <param name="Recalled">Whether the answer counts as knowing it: a correct question, or a flashcard graded Hard or better.</param>
public sealed record ReadinessAnswer(Guid ObjectiveId, bool? Correct, bool Recalled, DateTimeOffset At);

/// <summary>
/// An estimate of how ready the learner is for each exam domain, from their own practice answers:
/// <c>readiness = coverage × accuracy</c>.
/// <list type="bullet">
/// <item><b>Coverage</b>: the share of the domain's objectives recalled at least once (a correct question or a
/// flashcard graded Hard or better).</item>
/// <item><b>Accuracy</b>: the share of question answers that were right, with recent answers counting more (an
/// answer's weight halves every <see cref="HalfLifeDays"/> days) and shrunk toward 50% by a Beta(2, 2) prior, so
/// a handful of lucky answers can't read as 100%.</item>
/// </list>
/// Below <see cref="MinAnswers"/> question answers in the last <see cref="WindowDays"/> days a domain has no
/// score. The overall score weights domains by their exam weight and needs every domain to have one.
/// </summary>
public static class ReadinessCalculator
{
    public const int MinAnswers = 10;
    public const double HalfLifeDays = 14;
    public const int WindowDays = 90;
    public const double PriorCorrect = 2;
    public const double PriorWrong = 2;

    public static ReadinessReport Compute(IReadOnlyList<ReadinessDomain> domains, IReadOnlyList<ReadinessAnswer> answers, DateTimeOffset now)
    {
        var since = now.AddDays(-WindowDays);
        var recent = answers.Where(a => a.At >= since && a.At <= now).ToList();

        var scores = domains.Select(d =>
        {
            var objectives = d.ObjectiveIds.ToHashSet();
            var inDomain = recent.Where(a => objectives.Contains(a.ObjectiveId)).ToList();
            var covered = inDomain.Where(a => a.Recalled).Select(a => a.ObjectiveId).Distinct().Count();
            var questions = inDomain.Where(a => a.Correct is not null).ToList();

            double weighted = 0, weightedCorrect = 0;
            foreach (var answer in questions)
            {
                var weight = Math.Pow(0.5, (now - answer.At).TotalDays / HalfLifeDays);
                weighted += weight;
                weightedCorrect += answer.Correct == true ? weight : 0;
            }

            var coverage = objectives.Count == 0 ? 0 : (double)covered / objectives.Count;
            var accuracy = (weightedCorrect + PriorCorrect) / (weighted + PriorCorrect + PriorWrong);
            var enough = questions.Count >= MinAnswers;
            return new DomainReadiness(
                d.Id,
                d.Name,
                d.Weight,
                enough ? Percent(coverage * accuracy) : null,
                Percent(coverage),
                questions.Count == 0 ? null : Percent((double)questions.Count(q => q.Correct == true) / questions.Count),
                covered,
                objectives.Count,
                questions.Count);
        }).ToList();

        int? overall = null;
        if (scores.Count > 0 && scores.All(s => s.Score is not null))
        {
            var totalWeight = scores.Sum(s => s.Weight is > 0 ? s.Weight : 1);
            overall = (int)Math.Round(
                scores.Sum(s => (s.Weight is > 0 ? s.Weight : 1) * s.Score!.Value) / totalWeight, MidpointRounding.AwayFromZero);
        }

        return new ReadinessReport(overall, scores.Count(s => s.Score is not null), scores);
    }

    private static int Percent(double value) => (int)Math.Round(100 * value, MidpointRounding.AwayFromZero);
}

/// <param name="Overall">Weighted by the exam's domain weights; null until every domain has a score.</param>
public sealed record ReadinessReport(int? Overall, int DomainsScored, IReadOnlyList<DomainReadiness> Domains);

/// <param name="Score">0–100, or null with fewer than <see cref="ReadinessCalculator.MinAnswers"/> answers.</param>
/// <param name="Coverage">The share of objectives recalled at least once, 0–100.</param>
/// <param name="Accuracy">The plain share of question answers that were right, 0–100, before weighting; null without answers.</param>
/// <param name="Answers">Question answers in the window, which is what the score's confidence rests on.</param>
public sealed record DomainReadiness(
    Guid DomainId,
    string Name,
    decimal Weight,
    int? Score,
    int Coverage,
    int? Accuracy,
    int ObjectivesCovered,
    int ObjectivesTotal,
    int Answers);
