import { createContext, useCallback, useContext, useEffect, useLayoutEffect, useRef, useState, type ReactNode } from "react";
import { useNavigate } from "react-router";
import { markTourSeen, placeCard, tourSeen, tourSteps } from "../lib/tour";

const TourContext = createContext<() => void>(() => {});

/** Starts the welcome tour from anywhere under the provider. */
export const useStartTour = () => useContext(TourContext);

/** Shows the tour by itself the first time this browser opens the app, and lets any page start it again. */
export function TourProvider({ children }: { children: ReactNode }) {
  const [open, setOpen] = useState(() => !tourSeen());
  const start = useCallback(() => setOpen(true), []);
  const close = useCallback(() => {
    markTourSeen();
    setOpen(false);
  }, []);

  return (
    <TourContext.Provider value={start}>
      {children}
      {open && <WelcomeTour onClose={close} />}
    </TourContext.Provider>
  );
}

interface Rect {
  top: number;
  left: number;
  width: number;
  height: number;
}

function WelcomeTour({ onClose }: { onClose: () => void }) {
  const navigate = useNavigate();
  const [index, setIndex] = useState(0);
  const [target, setTarget] = useState<Rect | null>(null);
  const [position, setPosition] = useState({ top: 0, left: 0 });
  const card = useRef<HTMLDivElement>(null);
  const step = tourSteps[index]!;
  const last = index === tourSteps.length - 1;

  // Measures the highlighted element and places the card beside it, again whenever the window changes.
  useLayoutEffect(() => {
    const measure = () => {
      const element = step.target ? document.querySelector(`[data-tour="${step.target}"]`) : null;
      const box = element?.getBoundingClientRect();
      const visible = box && box.width > 0 && box.bottom > 0 && box.top < window.innerHeight;
      const rect = visible ? { top: box.top, left: box.left, width: box.width, height: box.height } : null;
      setTarget(rect);
      const size = card.current?.getBoundingClientRect() ?? { width: 360, height: 220 };
      setPosition(placeCard(rect, size, { width: window.innerWidth, height: window.innerHeight }));
    };
    measure();
    window.addEventListener("resize", measure);
    window.addEventListener("scroll", measure, true);
    return () => {
      window.removeEventListener("resize", measure);
      window.removeEventListener("scroll", measure, true);
    };
  }, [step]);

  // Focus moves to the card on every step, so keyboard and screen-reader users follow along.
  useEffect(() => card.current?.focus(), [index]);

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (e.key === "Escape") onClose();
      if (e.key === "ArrowRight" && !last) setIndex((i) => i + 1);
      if (e.key === "ArrowLeft" && index > 0) setIndex((i) => i - 1);
    };
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [index, last, onClose]);

  const finish = (to?: string) => {
    onClose();
    if (to) navigate(to);
  };

  return (
    <div className="tour" role="presentation">
      {/* With a target the spotlight's shadow dims the page and leaves the target clear; without one, the backdrop dims. */}
      <div className={`tour-backdrop${target ? "" : " dim"}`} onClick={onClose} />
      {target && (
        <div
          className="tour-spotlight"
          style={{ top: target.top - 6, left: target.left - 6, width: target.width + 12, height: target.height + 12 }}
          aria-hidden
        />
      )}
      <div
        ref={card}
        className="tour-card"
        role="dialog"
        aria-modal="true"
        aria-labelledby="tour-title"
        aria-describedby="tour-body"
        tabIndex={-1}
        style={{ top: position.top, left: position.left }}
      >
        <p className="muted small">
          {index + 1} of {tourSteps.length}
        </p>
        <h2 id="tour-title" className="tour-title">
          {step.title}
        </h2>
        <p id="tour-body">{step.body}</p>
        <div className="tour-dots" aria-hidden>
          {tourSteps.map((_, i) => (
            <span key={i} className={i === index ? "active" : undefined} />
          ))}
        </div>
        <div className="row-between">
          <button type="button" className="button ghost small" onClick={onClose}>
            {last ? "Close" : "Skip tour"}
          </button>
          <div className="actions">
            {index > 0 && (
              <button type="button" className="button ghost small" onClick={() => setIndex(index - 1)}>
                Back
              </button>
            )}
            {last ? (
              <button type="button" className="button primary small" onClick={() => finish("/certifications/new")}>
                Add a certification
              </button>
            ) : (
              <button type="button" className="button primary small" onClick={() => setIndex(index + 1)}>
                {index === 0 ? "Show me around" : "Next"}
              </button>
            )}
          </div>
        </div>
      </div>
    </div>
  );
}
