using CertCoach.Domain.Certifications;
using CertCoach.Application.Abstractions;
using CertCoach.Domain.Planning;
using CertCoach.Domain.Users;
using CertCoach.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CertCoach.Infrastructure.Tests;

[Collection(PostgresDatabase.Name)]
public class PersistenceTests(PostgresFixture postgres)
{
    private static readonly DateOnly Monday = new(2026, 11, 2);
    private static readonly DateTimeOffset Now = new(2026, 11, 2, 8, 0, 0, TimeSpan.Zero);

    private readonly Guid _owner = Guid.NewGuid();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Migrations_enable_the_pgvector_extension()
    {
        await using var db = postgres.CreateContext();

        var extensions = await db.Database
            .SqlQuery<string>($"SELECT extname AS \"Value\" FROM pg_extension")
            .ToListAsync(Ct);

        extensions.ShouldContain("vector");
    }

    [Fact]
    public async Task A_certification_outline_round_trips_in_order()
    {
        var certification = await SaveCertificationAsync();

        await using var db = postgres.CreateContext();
        var loaded = await LoadCertificationAsync(db, certification.Id);

        loaded.ExamCode.ShouldBe("DEMO-101");
        loaded.Domains.Select(d => d.Name).ShouldBe(["Plan solutions", "Build solutions", "Monitor solutions"]);
        loaded.Domains[0].WeightMinPercent.ShouldBe(45);
        loaded.Domains[0].SkillAreas.Single().Objectives.Select(o => o.Text)
            .ShouldBe(["Plan solutions objective 1", "Plan solutions objective 2", "Plan solutions objective 3"]);
    }

    [Fact]
    public async Task Exam_codes_are_unique_per_owner()
    {
        await SaveCertificationAsync();

        await using var db = postgres.CreateContext();
        db.Certifications.Add(Certification.Create(_owner, "demo-101", "Duplicate", "Contoso", Now));

        await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync(Ct));
    }

    [Fact]
    public async Task A_plan_round_trips_with_sessions_and_objective_ids()
    {
        var certification = await SaveCertificationAsync();
        var (plan, _) = StudyPlan.Create(_owner, certification, new PlanSettings(Monday, Monday.AddDays(28), 5m, StudyDays.Everyday), Now);
        await SavePlanAsync(plan);

        await using var db = postgres.CreateContext();
        var loaded = await db.StudyPlans.Include(p => p.Sessions).SingleAsync(p => p.Id == plan.Id, Ct);

        loaded.Settings.ShouldBe(plan.Settings);
        loaded.Shortfall.ShouldBeNull();
        loaded.Sessions.Select(Describe).ShouldBe(plan.Sessions.Select(Describe));
    }

    [Fact]
    public async Task A_shortfall_is_persisted()
    {
        var certification = await SaveCertificationAsync();
        var (plan, _) = StudyPlan.Create(_owner, certification, new PlanSettings(Monday, Monday.AddDays(3), 2m, StudyDays.Everyday), Now);
        await SavePlanAsync(plan);

        await using var db = postgres.CreateContext();
        var loaded = await db.StudyPlans.SingleAsync(p => p.Id == plan.Id, Ct);

        loaded.Shortfall.ShouldNotBeNull();
        loaded.Shortfall.ShouldBe(plan.Shortfall);
    }

    [Fact]
    public async Task Rescheduling_a_loaded_plan_inserts_and_deletes_the_right_sessions()
    {
        var certification = await SaveCertificationAsync();
        var (plan, _) = StudyPlan.Create(_owner, certification, new PlanSettings(Monday, Monday.AddDays(28), 5m, StudyDays.Everyday), Now);
        await SavePlanAsync(plan);

        List<string> expected;
        await using (var db = postgres.CreateContext())
        {
            var tracked = await db.StudyPlans.Include(p => p.Sessions).SingleAsync(p => p.Id == plan.Id, Ct);
            tracked.MarkSessionMissed(tracked.Sessions[0].Id, Monday);
            await db.SaveChangesAsync(Ct);
            expected = tracked.Sessions.Select(Describe).ToList();
        }

        await using var verify = postgres.CreateContext();
        var reloaded = await verify.StudyPlans.Include(p => p.Sessions).SingleAsync(p => p.Id == plan.Id, Ct);
        reloaded.Sessions.Select(Describe).ShouldBe(expected);
        reloaded.Sessions.Count(s => s.Status == SessionStatus.Missed).ShouldBe(1);
    }

    [Fact]
    public async Task The_owner_filter_hides_other_users_rows()
    {
        var certification = await SaveCertificationAsync();
        var (plan, _) = StudyPlan.Create(_owner, certification, new PlanSettings(Monday, Monday.AddDays(28), 5m, StudyDays.Everyday), Now);
        await SavePlanAsync(plan);

        await using var asOwner = postgres.CreateContext(new FixedUser(_owner));
        await using var asStranger = postgres.CreateContext(new FixedUser(Guid.NewGuid()));

        (await asOwner.Certifications.AnyAsync(c => c.Id == certification.Id, Ct)).ShouldBeTrue();
        (await asStranger.Certifications.AnyAsync(c => c.Id == certification.Id, Ct)).ShouldBeFalse();
        (await asStranger.StudyPlans.AnyAsync(p => p.Id == plan.Id, Ct)).ShouldBeFalse();
    }

    private async Task<Certification> SaveCertificationAsync()
    {
        await EnsureOwnerAsync();
        var certification = Certification.Create(_owner, "DEMO-101", "Demo Cert", "Contoso", Now);
        foreach (var (name, min, max) in new[] { ("Plan solutions", 45, 55), ("Build solutions", 25, 35), ("Monitor solutions", 15, 25) })
        {
            var area = certification.AddDomain(name, min, max).AddSkillArea($"{name} skills");
            for (var i = 1; i <= 3; i++)
            {
                area.AddObjective($"{name} objective {i}");
            }
        }

        await using var db = postgres.CreateContext();
        db.Certifications.Add(certification);
        await db.SaveChangesAsync(Ct);
        return certification;
    }

    private async Task EnsureOwnerAsync()
    {
        await using var db = postgres.CreateContext();
        if (!await db.Users.AnyAsync(u => u.Id == _owner, Ct))
        {
            db.Users.Add(User.Create("test", _owner.ToString(), "Test learner", null, Now, _owner));
            await db.SaveChangesAsync(Ct);
        }
    }

    private async Task SavePlanAsync(StudyPlan plan)
    {
        await using var db = postgres.CreateContext();
        db.StudyPlans.Add(plan);
        await db.SaveChangesAsync(Ct);
    }

    private static Task<Certification> LoadCertificationAsync(CertCoachDbContext db, Guid id) =>
        db.Certifications
            .Include(c => c.Domains).ThenInclude(d => d.SkillAreas).ThenInclude(s => s.Objectives)
            .SingleAsync(c => c.Id == id, Ct);

    private static string Describe(StudySession s) =>
        $"{s.Id}|{s.Sequence}|{s.ScheduledDate}|{s.Kind}|{s.Status}|{s.FocusDomainId}|{string.Join(',', s.ObjectiveIds)}";

    private sealed record FixedUser(Guid UserId) : ICurrentUser
    {
        public TimeZoneInfo TimeZone => TimeZoneInfo.Utc;
    }
}
