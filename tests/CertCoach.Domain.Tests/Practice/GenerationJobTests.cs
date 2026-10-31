using CertCoach.Domain.Common;
using CertCoach.Domain.Practice;

namespace CertCoach.Domain.Tests.Practice;

public class GenerationJobTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 2, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Works_through_its_objectives_in_order_and_adds_up_progress()
    {
        Guid[] objectives = [Guid.NewGuid(), Guid.NewGuid()];
        var job = GenerationJob.Create(Guid.NewGuid(), Guid.NewGuid(), [objectives[0], objectives[1], objectives[0]], 5, Now);

        job.ObjectiveIds.ShouldBe(objectives);
        job.NextObjective.ShouldBe(objectives[0]);
        job.RecordObjective(4, 1, hadMaterial: true, Now);
        job.RecordObjective(0, 0, hadMaterial: false, Now.AddMinutes(1));

        job.NextObjective.ShouldBeNull();
        (job.ItemsCreated, job.ItemsRejected, job.ObjectivesWithoutMaterial).ShouldBe((4, 1, 1));
        job.HeartbeatAt.ShouldBe(Now.AddMinutes(1));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(11)]
    public void Limits_the_items_per_objective(int perObjective)
    {
        Should.Throw<DomainException>(() => GenerationJob.Create(Guid.NewGuid(), Guid.NewGuid(), [Guid.NewGuid()], perObjective, Now));
        Should.Throw<DomainException>(() => GenerationJob.Create(Guid.NewGuid(), Guid.NewGuid(), [], 5, Now));
    }

    [Fact]
    public void A_cancelled_run_takes_no_more_progress_and_cancelling_a_finished_one_changes_nothing()
    {
        var job = GenerationJob.Create(Guid.NewGuid(), Guid.NewGuid(), [Guid.NewGuid()], 5, Now);
        job.Cancel(Now);

        Should.Throw<DomainException>(() => job.RecordObjective(1, 0, true, Now));

        var done = GenerationJob.Create(Guid.NewGuid(), Guid.NewGuid(), [Guid.NewGuid()], 5, Now);
        done.Complete(Now);
        done.Cancel(Now);
        done.Status.ShouldBe(GenerationStatus.Completed);
    }
}
