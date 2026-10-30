import { fireEvent, render, screen } from "@testing-library/react";
import type { DomainDraft } from "../api/client";
import { OutlineEditor } from "./OutlineEditor";

const outline: DomainDraft[] = [
  {
    name: "Plan",
    weightMinPercent: 40,
    weightMaxPercent: 60,
    skillAreas: [{ name: "Design", objectives: ["Draw the diagram", "Pick services"] }],
  },
  { name: "Run", weightMinPercent: 40, weightMaxPercent: 60, skillAreas: [] },
];

describe("OutlineEditor", () => {
  it("summarises domains, objectives and total weight", () => {
    render(<OutlineEditor value={outline} onChange={() => {}} />);

    expect(screen.getByText(/2 domains · 2 objectives · weights add up to about 100%/)).toBeInTheDocument();
  });

  it("edits a domain without mutating the original outline", () => {
    const onChange = vi.fn();
    render(<OutlineEditor value={outline} onChange={onChange} />);

    fireEvent.change(screen.getAllByLabelText("Domain name")[0]!, { target: { value: "Plan it" } });

    expect(onChange).toHaveBeenCalledWith([{ ...outline[0], name: "Plan it" }, outline[1]]);
    expect(outline[0]!.name).toBe("Plan");
  });

  it("trims and drops blank objectives when leaving the field", () => {
    const onChange = vi.fn();
    render(<OutlineEditor value={outline} onChange={onChange} />);

    fireEvent.blur(screen.getByLabelText("Objectives, one per line"), {
      target: { value: "  Draw the diagram \n\nPick services\n" },
    });

    expect(onChange.mock.lastCall?.[0][0].skillAreas[0].objectives).toEqual(["Draw the diagram", "Pick services"]);
  });

  it("removes a domain", () => {
    const onChange = vi.fn();
    render(<OutlineEditor value={outline} onChange={onChange} />);

    fireEvent.click(screen.getAllByLabelText("Remove domain")[1]!);

    expect(onChange).toHaveBeenCalledWith([outline[0]]);
  });
});
