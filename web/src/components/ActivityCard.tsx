import { useActivity } from "../api/hooks";
import { activityLevel, describeDay, toWeeks } from "../lib/activity";

const weekdays = ["Mon", "", "Wed", "", "Fri", "", ""];

/** The study streak and a calendar of the last weeks: one square per day, darker for more study. */
export function ActivityCard() {
  const activity = useActivity();
  if (!activity.data) return null;

  const { streak, days, reviewsThisWeek, sessionsThisWeek } = activity.data;
  const weeks = toWeeks(days);

  return (
    <section className="card activity-card" aria-label="Study activity">
      <div className="activity-summary">
        <div>
          <h3 className="card-title">Study streak</h3>
          <div className="big-number">
            {streak.current} <span className="activity-unit">{streak.current === 1 ? "day" : "days"}</span>
          </div>
        </div>
        <p className="muted small">
          {streak.current > 0 && !streak.studiedToday && <strong>Study today to keep it going. </strong>}
          {streak.current === 0 && "Review an item or finish a session to start one. "}
          Longest: {streak.longest} {streak.longest === 1 ? "day" : "days"} · This week: {reviewsThisWeek}{" "}
          {reviewsThisWeek === 1 ? "review" : "reviews"}, {sessionsThisWeek} {sessionsThisWeek === 1 ? "session" : "sessions"}
        </p>
      </div>

      <div className="activity-grid" role="img" aria-label={`Study activity over the last ${weeks.length} weeks`}>
        <div className="activity-week activity-labels" aria-hidden>
          {weekdays.map((d, i) => (
            <span key={i}>{d}</span>
          ))}
        </div>
        {weeks.map((week) => (
          <div key={week[0]!.date} className="activity-week">
            {week.map((day) => (
              <span key={day.date} className={`activity-day level-${activityLevel(day)}`} title={describeDay(day)} />
            ))}
          </div>
        ))}
      </div>
    </section>
  );
}
