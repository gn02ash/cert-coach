import { useEffect, useRef, useState, type FormEvent } from "react";
import { Link } from "react-router";
import type { AdviceView, ProfileDraft, RecommendationView } from "../api/client";
import {
  useAdvice,
  useAiStatus,
  useCareerProfile,
  useDeleteCareerProfile,
  useReadCv,
  useRecommendations,
  useSaveCareerProfile,
} from "../api/hooks";
import { Alert, EmptyState, ProgressBar, Warnings } from "../components/ui";
import { careerRoles, joinList, orderByAdvice, splitList } from "../lib/career";
import { formatLongDate } from "../lib/dates";
import sampleCv from "../../../samples/sample-cv.md?raw";

interface ProfileForm {
  skills: string;
  roles: string[];
  held: string;
  years: string;
  targetRole: string;
}

/** The learner's career profile, read from a CV or typed in, and the certifications recommended for it. */
export function CareerPage() {
  const profile = useCareerProfile();

  if (profile.isPending) return <div className="page-loading">Loading…</div>;
  if (profile.error) return <p className="error-text">{profile.error.message}</p>;

  return (
    <div className="stack">
      <div className="page-head">
        <h1>Career</h1>
      </div>
      <p className="muted">
        Tell Cert Coach what you know and where you want to go, and it ranks certifications from a built-in catalogue for you.
      </p>
      <ProfileCard key={profile.data.updatedAt ?? "new"} />
      {profile.data.exists ? (
        <Recommendations />
      ) : (
        <EmptyState title="No recommendations yet">Read your CV or fill in the profile above, then save it.</EmptyState>
      )}
    </div>
  );
}

function ProfileCard() {
  const profile = useCareerProfile().data!;
  const ai = useAiStatus();
  const read = useReadCv();
  const save = useSaveCareerProfile();
  const remove = useDeleteCareerProfile();
  const fileInput = useRef<HTMLInputElement>(null);
  const [cvText, setCvText] = useState("");
  const [form, setForm] = useState<ProfileForm>({
    skills: joinList(profile.skills),
    roles: profile.roles,
    held: joinList(profile.heldCertifications),
    years: profile.yearsExperience?.toString() ?? "",
    targetRole: profile.targetRole ?? "",
  });
  const usesAi = ai.data?.chatAvailable ?? false;

  // A draft replaces what was read from the CV and keeps the target role, which a CV doesn't state.
  const applyDraft = (draft: ProfileDraft) =>
    setForm((f) => ({
      ...f,
      skills: joinList(draft.skills),
      roles: draft.roles,
      held: joinList(draft.heldCertifications),
      years: draft.yearsExperience?.toString() ?? "",
    }));
  const readText = (text: string) => read.mutate({ text }, { onSuccess: applyDraft });
  const readFile = (file: File | undefined) => file && read.mutate({ file }, { onSuccess: applyDraft });

  const submit = (e: FormEvent) => {
    e.preventDefault();
    save.mutate({
      skills: splitList(form.skills),
      roles: form.roles,
      heldCertifications: splitList(form.held),
      yearsExperience: form.years === "" ? null : Number(form.years),
      targetRole: form.targetRole || null,
    });
  };
  const toggleRole = (role: string) =>
    setForm((f) => ({ ...f, roles: f.roles.includes(role) ? f.roles.filter((r) => r !== role) : [...f.roles, role] }));

  return (
    <section className="card stack">
      <h2 className="card-title">Your profile</h2>

      <details className="stack" open={!profile.exists}>
        <summary>Read it from my CV</summary>
        <div className="stack cv-reader">
          <p className="muted small">
            {usesAi
              ? `Your CV's text is sent to ${ai.data?.chatProvider} to be read. Cert Coach doesn't store the CV, only the profile you save below.`
              : "Your CV is read on this server without AI, by matching known skills, job titles and exam codes. It isn't stored; only the profile you save below is."}
          </p>
          <textarea
            rows={5}
            value={cvText}
            onChange={(e) => setCvText(e.target.value)}
            placeholder="Paste the text of your CV…"
            aria-label="CV text"
          />
          <div className="actions">
            <button type="button" className="button primary" disabled={read.isPending || cvText.trim().length < 50} onClick={() => readText(cvText)}>
              {read.isPending ? "Reading…" : "Read this text"}
            </button>
            <button type="button" className="button ghost" disabled={read.isPending} onClick={() => fileInput.current?.click()}>
              Upload a PDF
            </button>
            <input
              ref={fileInput}
              type="file"
              accept="application/pdf"
              hidden
              aria-label="CV as a PDF"
              onChange={(e) => {
                readFile(e.target.files?.[0]);
                e.target.value = "";
              }}
            />
            <button
              type="button"
              className="button ghost"
              disabled={read.isPending}
              onClick={() => {
                setCvText(sampleCv);
                readText(sampleCv);
              }}
            >
              Use the sample CV
            </button>
          </div>
          {read.error && <Alert tone="error">{read.error.message}</Alert>}
          {read.data && <Warnings warnings={read.data.notes} />}
        </div>
      </details>

      <form className="stack" onSubmit={submit}>
        <label>
          Skills
          <span className="muted small">Technologies and methods you have used, separated by commas.</span>
          <textarea rows={3} value={form.skills} onChange={(e) => setForm({ ...form, skills: e.target.value })} placeholder="azure, c#, python, docker, sql" />
        </label>

        <fieldset className="role-picker">
          <legend>Roles you have worked in</legend>
          {careerRoles.map((role) => (
            <label key={role} className={`chip${form.roles.includes(role) ? " selected" : ""}`}>
              <input type="checkbox" checked={form.roles.includes(role)} onChange={() => toggleRole(role)} />
              {role}
            </label>
          ))}
        </fieldset>

        <div className="form-grid">
          <label>
            Years of experience
            <input type="number" min={0} max={60} value={form.years} onChange={(e) => setForm({ ...form, years: e.target.value })} />
          </label>
          <label className="span-2">
            Target role
            <input value={form.targetRole} onChange={(e) => setForm({ ...form, targetRole: e.target.value })} placeholder="AI engineer, cloud architect…" />
          </label>
          <label className="span-2">
            Certifications you hold
            <input value={form.held} onChange={(e) => setForm({ ...form, held: e.target.value })} placeholder="AZ-900, SAA-C03" />
          </label>
        </div>

        {save.error && <Alert tone="error">{save.error.message}</Alert>}
        <div className="actions">
          <button className="button primary" disabled={save.isPending}>
            {profile.exists ? "Save and update recommendations" : "Save and see recommendations"}
          </button>
          {profile.exists && (
            <button
              type="button"
              className="button ghost danger"
              disabled={remove.isPending}
              onClick={() => confirm("Delete your profile? Recommendations are removed with it.") && remove.mutate()}
            >
              Delete profile
            </button>
          )}
        </div>
      </form>
    </section>
  );
}

function Recommendations() {
  const recommendations = useRecommendations();
  const ai = useAiStatus();
  const advice = useAdvice();

  // Advice belongs to the list it was asked about; a changed profile clears it.
  const reset = advice.reset;
  useEffect(() => reset(), [recommendations.dataUpdatedAt, reset]);

  if (recommendations.isPending) return <div className="page-loading">Loading…</div>;
  if (recommendations.error) return <Alert tone="error">{recommendations.error.message}</Alert>;

  const { items, catalogueSize, catalogueCheckedOn } = recommendations.data;
  const ordered = orderByAdvice(items, advice.data);

  return (
    <section className="stack">
      <div className="row-between">
        <h2>Recommended for you</h2>
        {ai.data?.chatAvailable && items.length > 0 && (
          <button type="button" className="button ghost small" disabled={advice.isPending} onClick={() => advice.mutate()}>
            {advice.isPending ? "Asking…" : "Explain with AI"}
          </button>
        )}
      </div>

      {advice.error && <Alert tone="error">{advice.error.message}</Alert>}
      {advice.data?.overview && <Alert>{advice.data.overview}</Alert>}

      {items.length === 0 ? (
        <EmptyState title="Nothing matches this profile yet">Add a few skills or a role, and save.</EmptyState>
      ) : (
        <ol className="plain-list stack">
          {ordered.map((item, i) => (
            <RecommendationCard key={item.code} item={item} rank={i + 1} advice={advice.data} />
          ))}
        </ol>
      )}

      <p className="muted small">
        Chosen from a built-in catalogue of {catalogueSize} certifications, checked against the vendors' pages on {formatLongDate(catalogueCheckedOn)}.
        Exams change and retire, so check the official page before you book. Scores come from fixed rules: the skills you already have, your role and
        target role, your experience, and the certifications you hold.
      </p>
    </section>
  );
}

function RecommendationCard({ item, rank, advice }: { item: RecommendationView; rank: number; advice: AdviceView | undefined }) {
  const rationale = advice?.items.find((a) => a.code === item.code)?.rationale;
  const addUrl = `/certifications/new?${new URLSearchParams({ examCode: item.code, name: item.name, vendor: item.vendor, url: item.url })}`;

  return (
    <li className="card stack recommendation">
      <div className="row-between">
        <div>
          <p className="muted small">
            {rank}. {item.vendor} · {item.level}
          </p>
          <h3 className="recommendation-title">
            {item.code}: {item.name}
          </h3>
        </div>
        <span className="recommendation-score" aria-label={`Match ${item.score} out of 100`}>
          {item.score}
        </span>
      </div>
      <ProgressBar value={item.score} max={100} label={`${item.code} match`} />
      <p>{item.summary}</p>
      {rationale && <p className="review-back">{rationale}</p>}
      <ul className="plain-list small recommendation-reasons">
        {item.roleMatch && (
          <li>
            <strong>Role:</strong> suits {item.matchesTargetRole ? "the role you're aiming for" : "the work you do"} ({item.roleMatch}).
          </li>
        )}
        {item.matchedSkills.length > 0 && (
          <li>
            <strong>You already know:</strong> {joinList(item.matchedSkills)}.
          </li>
        )}
        {item.newSkills.length > 0 && (
          <li>
            <strong>You'd learn:</strong> {joinList(item.newSkills)}.
          </li>
        )}
        {item.nextStepAfter && (
          <li>
            <strong>Next step:</strong> follows {item.nextStepAfter}, which you hold.
          </li>
        )}
        {item.usuallyAfter.length > 0 && (
          <li>
            <strong>Usually taken after:</strong> {item.usuallyAfter.join(" or ")}.
          </li>
        )}
      </ul>
      <div className="actions">
        {item.addedCertificationId ? (
          <Link className="button ghost small" to={`/certifications/${item.addedCertificationId}`}>
            In your certifications
          </Link>
        ) : (
          <Link className="button primary small" to={addUrl}>
            Add to my certifications
          </Link>
        )}
        <a className="button ghost small" href={item.url} target="_blank" rel="noreferrer">
          Official page
        </a>
      </div>
    </li>
  );
}
