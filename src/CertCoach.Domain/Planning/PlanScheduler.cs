using CertCoach.Domain.Common;

namespace CertCoach.Domain.Planning;

/// <summary>
/// Builds and repairs study schedules. Pure and deterministic: the same inputs always give the same plan.
/// </summary>
/// <remarks>
/// Priority when time is short: every objective gets a Learn session first, then mock exams, then the
/// review buffer. Learn sessions are interleaved across domains in proportion to their exam weight, so
/// heavy domains are spread over the calendar instead of studied in one block.
/// </remarks>
public static class PlanScheduler
{
    public static ScheduleResult Generate(PlanSettings settings, IReadOnlyList<OutlineDomain> outline)
    {
        var domains = outline.Where(d => d.ObjectiveIds.Count > 0).ToList();
        if (domains.Count == 0)
        {
            throw new DomainException("The outline has no objectives to schedule.");
        }

        var cadence = SessionCadence.For(settings.HoursPerWeek, settings.StudyDays);
        var slots = Slots(settings.StartDate, settings.ExamDate, cadence, usedOnFirstDay: 0);
        var objectiveCount = domains.Sum(d => d.ObjectiveIds.Count);

        var spare = Math.Max(0, slots.Count - objectiveCount);
        var mocks = Math.Min(DesiredMockExams(slots.Count), spare);
        var reviews = Math.Min(DesiredReviews(slots.Count, settings.ReviewBufferPercent), spare - mocks);
        var learnSlots = slots.Count - mocks - reviews;

        var learn = BuildLearnItems(domains, learnSlots);
        var reviewFocus = WeightedSequence(domains, reviews);

        var warnings = new List<PlanWarning>();
        if (cadence.IsCapped)
        {
            warnings.Add(new PlanWarning(
                PlanWarningCode.HoursCapped,
                $"Capped at {SessionCadence.MaxSessionsPerDay} sessions of {SessionCadence.MaxSessionMinutes} min per study day."));
        }

        CapacityShortfall? shortfall = null;
        if (learnSlots < objectiveCount)
        {
            warnings.Add(slots.Count == 0 ? NoSlotsWarning() : PackedWarning());
            shortfall = Shortfall(settings, settings.StartDate, cadence, objectiveCount - learnSlots, slots.Count);
        }

        return new ScheduleResult(
            Layout(slots, learn, reviewFocus, mocks, cadence.SessionMinutes), warnings, shortfall);
    }

    /// <summary>
    /// Re-lays outstanding work onto the slots left before the exam.
    /// </summary>
    /// <param name="settings">The plan's settings.</param>
    /// <param name="today">The learner's current date.</param>
    /// <param name="sessionsUsedToday">Slots today already taken by completed or missed sessions.</param>
    /// <param name="carryOver">Newly missed sessions. Their Learn content goes to the front of the queue; missed Review and MockExam sessions are dropped.</param>
    /// <param name="upcoming">Planned sessions from today onward, in order. These are replaced by the result.</param>
    public static ScheduleResult Reschedule(
        PlanSettings settings,
        DateOnly today,
        int sessionsUsedToday,
        IReadOnlyList<SessionDraft> carryOver,
        IReadOnlyList<SessionDraft> upcoming)
    {
        var cadence = SessionCadence.For(settings.HoursPerWeek, settings.StudyDays);
        var from = today > settings.StartDate ? today : settings.StartDate;
        var slots = Slots(from, settings.ExamDate, cadence, from == today ? sessionsUsedToday : 0);

        var learn = carryOver.Concat(upcoming)
            .Where(s => s.Kind == SessionKind.Learn)
            .Select(s => s.ObjectiveIds)
            .ToList();
        var reviewFocus = upcoming.Where(s => s.Kind == SessionKind.Review).Select(s => s.FocusDomainId).ToList();
        var upcomingMocks = upcoming.Count(s => s.Kind == SessionKind.MockExam);

        if (learn.Count > slots.Count)
        {
            // Over capacity: the review buffer and mocks are gone; squeeze the remaining content in.
            var shortfall = Shortfall(settings, from, cadence, learn.Count - slots.Count, slots.Count);
            var packed = Chunk(learn, slots.Count)
                .Select(group => (IReadOnlyList<Guid>)group.SelectMany(ids => ids).Distinct().ToList())
                .ToList();
            return new ScheduleResult(
                Layout(slots, packed, [], 0, cadence.SessionMinutes),
                [slots.Count == 0 ? NoSlotsWarning() : PackedWarning()],
                shortfall);
        }

        // The review buffer absorbs missed work first; mocks are kept as long as possible.
        var mocks = Math.Min(upcomingMocks, slots.Count - learn.Count);
        var reviews = Math.Min(reviewFocus.Count, slots.Count - learn.Count - mocks);

        return new ScheduleResult(
            Layout(slots, learn, reviewFocus.Take(reviews).ToList(), mocks, cadence.SessionMinutes),
            [],
            null);
    }

    internal static int DesiredMockExams(int slots) => slots switch
    {
        >= 20 => 2,
        >= 6 => 1,
        _ => 0,
    };

    internal static int DesiredReviews(int slots, int reviewBufferPercent) =>
        reviewBufferPercent == 0 || slots < 10
            ? 0
            : Math.Max(1, (int)Math.Round(slots * reviewBufferPercent / 100.0, MidpointRounding.AwayFromZero));

    /// <summary>One entry per session slot from <paramref name="from"/> up to the day before the exam.</summary>
    private static List<DateOnly> Slots(DateOnly from, DateOnly examDate, SessionCadence cadence, int usedOnFirstDay)
    {
        var slots = new List<DateOnly>();
        for (var day = from; day < examDate; day = day.AddDays(1))
        {
            var count = cadence.SessionsOn(day.DayOfWeek) - (day == from ? usedOnFirstDay : 0);
            for (var i = 0; i < count; i++)
            {
                slots.Add(day);
            }
        }

        return slots;
    }

    private static List<IReadOnlyList<Guid>> BuildLearnItems(IReadOnlyList<OutlineDomain> domains, int learnSlots)
    {
        if (learnSlots <= 0)
        {
            return [];
        }

        if (learnSlots < domains.Count)
        {
            // Fewer sessions than domains: walk the outline in order, several objectives per session.
            return Chunk(domains.SelectMany(d => d.ObjectiveIds).ToList(), learnSlots);
        }

        var allocation = AllocateSlots(domains, learnSlots);
        var queues = domains
            .Select((d, i) => allocation[i] >= d.ObjectiveIds.Count
                ? Repeat(d.ObjectiveIds, allocation[i])
                : Chunk(d.ObjectiveIds, allocation[i]))
            .ToList();

        return Interleave(queues);
    }

    /// <summary>
    /// Splits sessions across domains in proportion to weight, after guaranteeing each objective one
    /// session (or, when packing, each domain one session). Greedy largest-deficit keeps the sum exact.
    /// </summary>
    private static int[] AllocateSlots(IReadOnlyList<OutlineDomain> domains, int learnSlots)
    {
        var objectiveCount = domains.Sum(d => d.ObjectiveIds.Count);
        var packing = learnSlots < objectiveCount;
        var weights = EffectiveWeights(domains);
        var totalWeight = weights.Sum();

        var allocation = domains.Select(d => packing ? 1 : d.ObjectiveIds.Count).ToArray();
        var targets = weights.Select(w => learnSlots * w / totalWeight).ToArray();

        for (var remaining = learnSlots - allocation.Sum(); remaining > 0; remaining--)
        {
            var best = -1;
            for (var i = 0; i < domains.Count; i++)
            {
                if (packing && allocation[i] >= domains[i].ObjectiveIds.Count)
                {
                    continue;
                }

                if (best < 0 || targets[i] - allocation[i] > targets[best] - allocation[best])
                {
                    best = i;
                }
            }

            allocation[best]++;
        }

        return allocation;
    }

    /// <summary>
    /// One objective per session, in passes: every objective once, then a second pass, and so on.
    /// Repetition comes only after first contact with the whole domain; a partial last pass covers
    /// the earliest objectives.
    /// </summary>
    private static List<IReadOnlyList<Guid>> Repeat(IReadOnlyList<Guid> objectives, int sessions)
    {
        var result = new List<IReadOnlyList<Guid>>(sessions);
        for (var i = 0; i < sessions; i++)
        {
            result.Add([objectives[i % objectives.Count]]);
        }

        return result;
    }

    /// <summary>Splits a list into <paramref name="groups"/> contiguous groups whose sizes differ by at most one.</summary>
    private static List<IReadOnlyList<T>> Chunk<T>(IReadOnlyList<T> items, int groups)
    {
        var result = new List<IReadOnlyList<T>>(groups);
        var index = 0;
        for (var g = 0; g < groups; g++)
        {
            var size = (items.Count / groups) + (g < items.Count % groups ? 1 : 0);
            result.Add(items.Skip(index).Take(size).ToList());
            index += size;
        }

        return result;
    }

    /// <summary>Smooth weighted round-robin over the domain queues, weighted by queue length.</summary>
    private static List<IReadOnlyList<Guid>> Interleave(IReadOnlyList<List<IReadOnlyList<Guid>>> queues)
    {
        var total = queues.Sum(q => q.Count);
        var current = new long[queues.Count];
        var taken = new int[queues.Count];
        var result = new List<IReadOnlyList<Guid>>(total);

        for (var step = 0; step < total; step++)
        {
            var best = -1;
            for (var i = 0; i < queues.Count; i++)
            {
                if (taken[i] == queues[i].Count)
                {
                    continue;
                }

                current[i] += queues[i].Count;
                if (best < 0 || current[i] > current[best])
                {
                    best = i;
                }
            }

            current[best] -= total;
            result.Add(queues[best][taken[best]++]);
        }

        return result;
    }

    /// <summary>Picks <paramref name="count"/> domains so heavier domains come up proportionally more often.</summary>
    private static List<Guid?> WeightedSequence(IReadOnlyList<OutlineDomain> domains, int count)
    {
        var weights = EffectiveWeights(domains);
        var total = weights.Sum();
        var current = new decimal[domains.Count];
        var result = new List<Guid?>(count);

        for (var step = 0; step < count; step++)
        {
            var best = 0;
            for (var i = 0; i < domains.Count; i++)
            {
                current[i] += weights[i];
                if (current[i] > current[best])
                {
                    best = i;
                }
            }

            current[best] -= total;
            result.Add(domains[best].DomainId);
        }

        return result;
    }

    /// <summary>Domain weights, falling back to equal weights when none are published.</summary>
    private static decimal[] EffectiveWeights(IReadOnlyList<OutlineDomain> domains) =>
        domains.All(d => d.Weight <= 0)
            ? domains.Select(_ => 1m).ToArray()
            : domains.Select(d => Math.Max(0, d.Weight)).ToArray();

    private static List<SessionDraft> Layout(
        IReadOnlyList<DateOnly> slots,
        IReadOnlyList<IReadOnlyList<Guid>> learn,
        IReadOnlyList<Guid?> reviewFocus,
        int mocks,
        int minutes)
    {
        var drafts = new List<SessionDraft>(slots.Count);
        for (var i = 0; i < learn.Count; i++)
        {
            drafts.Add(new SessionDraft(slots[i], minutes, SessionKind.Learn, learn[i]));
        }

        // Review and mock exams sit at the end, right before the exam.
        var tail = slots.Count - reviewFocus.Count - mocks;
        for (var i = 0; i < reviewFocus.Count; i++)
        {
            drafts.Add(new SessionDraft(slots[tail + i], minutes, SessionKind.Review, [], reviewFocus[i]));
        }

        for (var i = 0; i < mocks; i++)
        {
            drafts.Add(new SessionDraft(slots[tail + reviewFocus.Count + i], minutes, SessionKind.MockExam, []));
        }

        return drafts;
    }

    private static CapacityShortfall Shortfall(
        PlanSettings settings, DateOnly from, SessionCadence cadence, int missingSessions, int availableSessions)
    {
        var weeks = Math.Max(1, settings.ExamDate.DayNumber - from.DayNumber) / 7m;
        var neededHours = (availableSessions + missingSessions) * cadence.SessionMinutes / 60m / weeks;
        var suggestedHours = Math.Max(settings.HoursPerWeek + 0.5m, Math.Ceiling(neededHours * 2) / 2);

        var examDate = settings.ExamDate;
        for (var remaining = missingSessions; remaining > 0; examDate = examDate.AddDays(1))
        {
            remaining -= cadence.SessionsOn(examDate.DayOfWeek);
        }

        return new CapacityShortfall(missingSessions, suggestedHours, examDate);
    }

    private static PlanWarning PackedWarning() => new(
        PlanWarningCode.ObjectivesPacked,
        "Not enough sessions for one per objective, so some sessions cover several objectives.");

    private static PlanWarning NoSlotsWarning() => new(
        PlanWarningCode.NoStudySlots,
        "No study days fall between the start date and the exam.");
}
