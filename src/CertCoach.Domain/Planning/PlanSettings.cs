using CertCoach.Domain.Common;

namespace CertCoach.Domain.Planning;

/// <summary>The learner's constraints for a study plan.</summary>
public sealed record PlanSettings
{
    public const int DefaultReviewBufferPercent = 10;

    public PlanSettings(
        DateOnly startDate,
        DateOnly examDate,
        decimal hoursPerWeek,
        StudyDays studyDays,
        int reviewBufferPercent = DefaultReviewBufferPercent)
    {
        if (examDate <= startDate)
        {
            throw new DomainException("The exam date must be after the start date.");
        }

        if (hoursPerWeek is <= 0 or > 60)
        {
            throw new DomainException("Hours per week must be between 0 and 60.");
        }

        if ((studyDays & StudyDays.Everyday) == StudyDays.None)
        {
            throw new DomainException("Pick at least one study day.");
        }

        if (reviewBufferPercent is < 0 or > 50)
        {
            throw new DomainException("The review buffer must be between 0% and 50%.");
        }

        StartDate = startDate;
        ExamDate = examDate;
        HoursPerWeek = hoursPerWeek;
        StudyDays = studyDays & StudyDays.Everyday;
        ReviewBufferPercent = reviewBufferPercent;
    }

    public DateOnly StartDate { get; }
    public DateOnly ExamDate { get; }
    public decimal HoursPerWeek { get; }
    public StudyDays StudyDays { get; }
    public int ReviewBufferPercent { get; }
}
