import { useState, type FormEvent } from "react";
import type { PracticeItemView } from "../api/client";
import { useUpdatePracticeItem } from "../api/hooks";
import { Alert } from "./ui";

const letters = "ABCDEFGH";
const maxOptions = 8;
const minOptions = 3;

/** Edits a question or flashcard in place. The kind and the review schedule stay as they are. */
export function ItemEditor({ item, onDone }: { item: PracticeItemView; onDone: () => void }) {
  const update = useUpdatePracticeItem();
  const flashcard = item.kind === "Flashcard";
  const single = item.kind === "SingleChoice";
  const [prompt, setPrompt] = useState(item.prompt);
  const [answer, setAnswer] = useState(item.answer);
  const [options, setOptions] = useState(item.options);
  const [correct, setCorrect] = useState(item.correctOptions);

  const toggleCorrect = (i: number) =>
    setCorrect((c) => (single ? [i] : c.includes(i) ? c.filter((x) => x !== i) : [...c, i].sort((a, b) => a - b)));
  const removeOption = (i: number) => {
    setOptions((o) => o.filter((_, x) => x !== i));
    // Later options move up one place.
    setCorrect((c) => c.filter((x) => x !== i).map((x) => (x > i ? x - 1 : x)));
  };

  const submit = (e: FormEvent) => {
    e.preventDefault();
    update.mutate(
      {
        id: item.id,
        request: { prompt, answer, options: flashcard ? null : options, correctOptions: flashcard ? null : correct },
      },
      { onSuccess: onDone },
    );
  };

  return (
    <form className="stack item-editor" onSubmit={submit}>
      <label>
        {flashcard ? "Front" : "Question"}
        <textarea rows={3} required value={prompt} onChange={(e) => setPrompt(e.target.value)} />
      </label>

      {!flashcard && (
        <fieldset className="stack item-editor-options">
          <legend>{single ? "Options: mark the correct one" : "Options: mark every correct one"}</legend>
          {options.map((option, i) => (
            <div key={i} className="item-editor-option">
              <input
                type={single ? "radio" : "checkbox"}
                name={`correct-${item.id}`}
                checked={correct.includes(i)}
                onChange={() => toggleCorrect(i)}
                aria-label={`Option ${letters[i]} is correct`}
              />
              <input
                required
                value={option}
                onChange={(e) => setOptions((o) => o.map((x, n) => (n === i ? e.target.value : x)))}
                aria-label={`Option ${letters[i]}`}
              />
              <button
                type="button"
                className="button ghost small"
                disabled={options.length <= minOptions}
                onClick={() => removeOption(i)}
                aria-label={`Remove option ${letters[i]}`}
              >
                ×
              </button>
            </div>
          ))}
          {options.length < maxOptions && (
            <button type="button" className="button ghost small" onClick={() => setOptions((o) => [...o, ""])}>
              + Option
            </button>
          )}
        </fieldset>
      )}

      <label>
        {flashcard ? "Back" : "Explanation"}
        <textarea rows={3} required={flashcard} value={answer} onChange={(e) => setAnswer(e.target.value)} />
      </label>

      {update.error && <Alert tone="error">{update.error.message}</Alert>}
      <div className="actions">
        <button className="button primary small" disabled={update.isPending}>
          Save
        </button>
        <button type="button" className="button ghost small" onClick={onDone}>
          Cancel
        </button>
      </div>
    </form>
  );
}
