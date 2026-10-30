import type { DomainDraft } from "../api/client";

type Props = { value: DomainDraft[]; onChange: (outline: DomainDraft[]) => void };

/** An editable tree of domains, skill areas and objectives. Every change produces a new outline. */
export function OutlineEditor({ value, onChange }: Props) {
  const updateDomain = (index: number, patch: Partial<DomainDraft>) =>
    onChange(value.map((d, i) => (i === index ? { ...d, ...patch } : d)));

  const total = value.reduce((sum, d) => sum + (Number(d.weightMinPercent) + Number(d.weightMaxPercent)) / 2, 0);

  return (
    <div className="outline-editor">
      <p className="muted small">
        {value.length} domains · {value.reduce((n, d) => n + d.skillAreas.reduce((m, s) => m + s.objectives.length, 0), 0)}{" "}
        objectives · weights add up to about {Math.round(total)}%
      </p>

      {value.map((domain, di) => (
        <fieldset key={di} className="domain-edit">
          <div className="domain-row">
            <input
              aria-label="Domain name"
              className="grow strong"
              value={domain.name}
              onChange={(e) => updateDomain(di, { name: e.target.value })}
            />
            <input
              aria-label="Minimum weight"
              type="number"
              min={0}
              max={100}
              className="pct"
              value={domain.weightMinPercent}
              onChange={(e) => updateDomain(di, { weightMinPercent: Number(e.target.value) })}
            />
            <span className="muted">–</span>
            <input
              aria-label="Maximum weight"
              type="number"
              min={0}
              max={100}
              className="pct"
              value={domain.weightMaxPercent}
              onChange={(e) => updateDomain(di, { weightMaxPercent: Number(e.target.value) })}
            />
            <span className="muted">%</span>
            <button type="button" className="icon-button" aria-label="Remove domain" onClick={() => onChange(value.filter((_, i) => i !== di))}>
              ×
            </button>
          </div>

          {domain.skillAreas.map((area, si) => {
            const updateArea = (patch: Partial<typeof area>) =>
              updateDomain(di, { skillAreas: domain.skillAreas.map((s, i) => (i === si ? { ...s, ...patch } : s)) });
            return (
              <div key={si} className="area-edit">
                <div className="domain-row">
                  <input aria-label="Skill area name" className="grow" value={area.name} onChange={(e) => updateArea({ name: e.target.value })} />
                  <button
                    type="button"
                    className="icon-button"
                    aria-label="Remove skill area"
                    onClick={() => updateDomain(di, { skillAreas: domain.skillAreas.filter((_, i) => i !== si) })}
                  >
                    ×
                  </button>
                </div>
                <textarea
                  aria-label="Objectives, one per line"
                  rows={Math.max(2, area.objectives.length)}
                  value={area.objectives.join("\n")}
                  onChange={(e) => updateArea({ objectives: e.target.value.split("\n") })}
                  onBlur={(e) => updateArea({ objectives: e.target.value.split("\n").map((o) => o.trim()).filter(Boolean) })}
                />
              </div>
            );
          })}

          <button
            type="button"
            className="button ghost small"
            onClick={() => updateDomain(di, { skillAreas: [...domain.skillAreas, { name: "New skill area", objectives: [] }] })}
          >
            + Skill area
          </button>
        </fieldset>
      ))}

      <button
        type="button"
        className="button ghost small"
        onClick={() => onChange([...value, { name: "New domain", weightMinPercent: 10, weightMaxPercent: 15, skillAreas: [] }])}
      >
        + Domain
      </button>
    </div>
  );
}
