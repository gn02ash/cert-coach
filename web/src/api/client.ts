import { readSse } from "../lib/sse";
import type { components } from "./schema";

type Schemas = components["schemas"];

export type AuthProvider = Schemas["AuthProvider"];
export type UserProfile = Schemas["UserProfile"];
export type DashboardView = Schemas["DashboardView"];
export type PlanDashboard = Schemas["PlanDashboard"];
export type SessionView = Schemas["SessionView"];
export type SessionKind = Schemas["SessionKind"];
export type CertificationSummary = Schemas["CertificationSummary"];
export type CertificationDetail = Schemas["CertificationDetail"];
export type CertificationStatus = Schemas["CertificationStatus"];
export type SaveCertificationRequest = Schemas["SaveCertificationRequest"];
export type OutlineDraft = Schemas["OutlineDraft"];
export type DomainDraft = Schemas["DomainDraft"];
export type OutlineImport = Schemas["OutlineImport"];
export type PlanRequest = Schemas["PlanRequest"];
export type PlanPreview = Schemas["PlanPreview"];
export type PlanView = Schemas["PlanView"];
export type PlanWarning = Schemas["PlanWarning"];
export type CapacityShortfall = Schemas["CapacityShortfall"];
export type DayOfWeek = Schemas["DayOfWeek"];
export type DocumentSummary = Schemas["DocumentSummary"];
export type DocumentStatus = Schemas["DocumentStatus"];
export type UpdateDocumentRequest = Schemas["UpdateDocumentRequest"];
export type AiCapabilities = Schemas["AiCapabilities"];
export type SearchResponse = Schemas["SearchResponse"];
export type SearchHit = Schemas["SearchHit"];
export type AskRequest = Schemas["AskRequest"];
export type AskTurn = Schemas["AskTurn"];
export type PracticeItemView = Schemas["PracticeItemView"];
export type PracticeItemStatus = Schemas["PracticeItemStatus"];
export type CreatePracticeItemRequest = Schemas["CreatePracticeItemRequest"];

/** The answer stream isn't described by OpenAPI, so these mirror the server's AskEvent records by hand. */
export interface AskSource {
  number: number;
  chunkId: string;
  documentId: string;
  documentTitle: string;
  headingPath: string;
  pageStart: number;
  pageEnd: number;
  text: string;
}
export type AnswerKind = "Ai" | "Quoted" | "NotCovered" | "Declined";
export type AskEvent =
  | { type: "sources"; sources: AskSource[] }
  | { type: "delta"; text: string }
  | { type: "done"; kind: AnswerKind; cited: number[]; notice: string | null };

/** An HTTP error, carrying the problem-details message from the API when there is one. */
export class ApiError extends Error {
  constructor(
    readonly status: number,
    message: string,
  ) {
    super(message);
  }
}

type Method = "GET" | "POST" | "PUT" | "DELETE";

/**
 * Calls the API on the same origin with the session cookie. State-changing requests carry the
 * X-CSRF header the API requires.
 */
export async function api<T>(path: string, method: Method = "GET", body?: unknown): Promise<T> {
  const headers: Record<string, string> = { Accept: "application/json" };
  if (body !== undefined) headers["Content-Type"] = "application/json";
  if (method !== "GET") headers["X-CSRF"] = "1";

  const response = await fetch(path, {
    method,
    headers,
    credentials: "same-origin",
    body: body === undefined ? undefined : JSON.stringify(body),
  });

  if (!response.ok) {
    throw new ApiError(response.status, await problemMessage(response));
  }

  return response.status === 204 ? (undefined as T) : ((await response.json()) as T);
}

/** Posts a multipart form, such as a file upload. The browser sets the multipart boundary. */
export async function upload<T>(path: string, form: FormData): Promise<T> {
  const response = await fetch(path, {
    method: "POST",
    headers: { Accept: "application/json", "X-CSRF": "1" },
    credentials: "same-origin",
    body: form,
  });

  if (!response.ok) {
    throw new ApiError(response.status, await problemMessage(response));
  }

  return (await response.json()) as T;
}

/** Streams an answer: calls onEvent for the sources, each piece of text, and the final summary. */
export async function askStream(request: AskRequest, onEvent: (event: AskEvent) => void, signal?: AbortSignal) {
  const response = await fetch("/api/ask", {
    method: "POST",
    headers: { Accept: "text/event-stream", "Content-Type": "application/json", "X-CSRF": "1" },
    credentials: "same-origin",
    body: JSON.stringify(request),
    signal,
  });

  if (!response.ok || !response.body) {
    throw new ApiError(response.status, await problemMessage(response));
  }

  for await (const message of readSse(response.body)) {
    onEvent({ type: message.event, ...JSON.parse(message.data) } as AskEvent);
  }
}

async function problemMessage(response: Response): Promise<string> {
  try {
    const problem = (await response.json()) as { detail?: string; title?: string };
    return problem.detail ?? problem.title ?? response.statusText;
  } catch {
    return response.statusText || `Request failed (${response.status})`;
  }
}
