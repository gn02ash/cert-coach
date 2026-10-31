import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  api,
  type AuthProvider,
  type CertificationDetail,
  type CertificationSummary,
  type AiCapabilities,
  type DashboardView,
  type DocumentSummary,
  type OutlineDraft,
  type OutlineImport,
  type CreatePracticeItemRequest,
  type PracticeItemStatus,
  type PracticeItemView,
  type GenerateItemsRequest,
  type GenerationJobView,
  type PlanPreview,
  type PlanRequest,
  type PlanView,
  type SearchResponse,
  type SaveCertificationRequest,
  type UpdateDocumentRequest,
  type UserProfile,
  upload,
} from "./client";

export const keys = {
  me: ["me"] as const,
  providers: ["providers"] as const,
  dashboard: ["dashboard"] as const,
  certifications: ["certifications"] as const,
  certification: (id: string) => ["certifications", id] as const,
  plan: (id: string) => ["plans", id] as const,
  documents: ["documents"] as const,
  practiceItems: (certificationId: string) => ["practice-items", certificationId] as const,
  generations: (certificationId: string) => ["generations", certificationId] as const,
  aiStatus: ["ai-status"] as const,
  search: (query: string, certificationId?: string) => ["search", query, certificationId ?? ""] as const,
};

export const useMe = () =>
  useQuery({ queryKey: keys.me, queryFn: () => api<UserProfile>("/auth/me"), retry: false });

export const useProviders = () =>
  useQuery({ queryKey: keys.providers, queryFn: () => api<AuthProvider[]>("/auth/providers") });

export const useDashboard = () =>
  useQuery({ queryKey: keys.dashboard, queryFn: () => api<DashboardView>("/api/dashboard") });

export const useCertifications = () =>
  useQuery({ queryKey: keys.certifications, queryFn: () => api<CertificationSummary[]>("/api/certifications") });

export const useCertification = (id: string | undefined) =>
  useQuery({
    queryKey: keys.certification(id ?? ""),
    queryFn: () => api<CertificationDetail>(`/api/certifications/${id}`),
    enabled: !!id,
  });

export const usePlan = (id: string | undefined) =>
  useQuery({ queryKey: keys.plan(id ?? ""), queryFn: () => api<PlanView>(`/api/plans/${id}`), enabled: !!id });

export function useLogout() {
  const client = useQueryClient();
  return useMutation({
    mutationFn: () => api<void>("/auth/logout", "POST"),
    onSuccess: () => client.clear(),
  });
}

export function useSetTimeZone() {
  const client = useQueryClient();
  return useMutation({
    mutationFn: (timeZone: string) => api<UserProfile>("/auth/me/time-zone", "PUT", { timeZone }),
    onSuccess: (profile) => {
      client.setQueryData(keys.me, profile);
      void client.invalidateQueries({ queryKey: keys.dashboard });
    },
  });
}

export const useParseOutline = () =>
  useMutation({
    mutationFn: ({ text, ai }: { text: string; ai?: boolean }) =>
      api<OutlineDraft>(ai ? "/api/outlines/parse-ai" : "/api/outlines/parse", "POST", { text }),
  });

export const useImportOutline = () =>
  useMutation({ mutationFn: (url: string) => api<OutlineImport>("/api/outlines/import-url", "POST", { url }) });

export function useSaveCertification(id: string | undefined) {
  const client = useQueryClient();
  return useMutation({
    mutationFn: (request: SaveCertificationRequest) =>
      id
        ? api<CertificationDetail>(`/api/certifications/${id}`, "PUT", request)
        : api<CertificationDetail>("/api/certifications", "POST", request),
    onSuccess: () => client.invalidateQueries({ queryKey: keys.certifications }),
  });
}

export function useDeleteCertification() {
  const client = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => api<void>(`/api/certifications/${id}`, "DELETE"),
    onSuccess: () => client.invalidateQueries({ queryKey: keys.certifications }),
  });
}

export const usePreviewPlan = () =>
  useMutation({ mutationFn: (request: PlanRequest) => api<PlanPreview>("/api/plans/preview", "POST", request) });

/** Every plan-changing call returns the updated plan; refresh everything that summarises it. */
function usePlanMutation<TArgs>(call: (args: TArgs) => Promise<PlanView>) {
  const client = useQueryClient();
  return useMutation({
    mutationFn: call,
    onSuccess: (plan) => {
      client.setQueryData(keys.plan(plan.id), plan);
      void client.invalidateQueries({ queryKey: keys.dashboard });
      void client.invalidateQueries({ queryKey: keys.certifications });
    },
  });
}

export const useCreatePlan = () =>
  usePlanMutation((request: PlanRequest) => api<PlanView>("/api/plans", "POST", request));

export const useSessionAction = () =>
  usePlanMutation(({ planId, sessionId, action }: { planId: string; sessionId: string; action: "complete" | "miss" }) =>
    api<PlanView>(`/api/plans/${planId}/sessions/${sessionId}/${action}`, "POST"),
  );

export const useRescheduleOverdue = () =>
  usePlanMutation((planId: string) => api<PlanView>(`/api/plans/${planId}/reschedule-overdue`, "POST"));

export const useArchivePlan = () =>
  usePlanMutation((planId: string) => api<PlanView>(`/api/plans/${planId}/archive`, "POST"));

export const useAiStatus = () =>
  useQuery({ queryKey: keys.aiStatus, queryFn: () => api<AiCapabilities>("/api/ai/status"), staleTime: Infinity });

/** Polls while any document is still being processed, so statuses update without a refresh. */
export const useDocuments = () =>
  useQuery({
    queryKey: keys.documents,
    queryFn: () => api<DocumentSummary[]>("/api/documents"),
    refetchInterval: (query) =>
      query.state.data?.some((d) => d.status === "Pending" || d.status === "Processing") ? 2000 : false,
  });

function useDocumentMutation<TArgs, TResult>(call: (args: TArgs) => Promise<TResult>) {
  const client = useQueryClient();
  return useMutation({
    mutationFn: call,
    onSuccess: () => client.invalidateQueries({ queryKey: keys.documents }),
  });
}

export const useUploadDocument = () =>
  useDocumentMutation(({ file, certificationId }: { file: File; certificationId?: string }) => {
    const form = new FormData();
    form.append("file", file);
    if (certificationId) form.append("certificationId", certificationId);
    return upload<DocumentSummary>("/api/documents", form);
  });

export const useUpdateDocument = () =>
  useDocumentMutation(({ id, request }: { id: string; request: UpdateDocumentRequest }) =>
    api<DocumentSummary>(`/api/documents/${id}`, "PUT", request),
  );

export const useRetryDocument = () =>
  useDocumentMutation((id: string) => api<DocumentSummary>(`/api/documents/${id}/retry`, "POST"));

export const useDeleteDocument = () => useDocumentMutation((id: string) => api<void>(`/api/documents/${id}`, "DELETE"));

export const useSearch = (query: string, certificationId?: string) =>
  useQuery({
    queryKey: keys.search(query, certificationId),
    queryFn: () => {
      const params = new URLSearchParams({ q: query });
      if (certificationId) params.set("certificationId", certificationId);
      return api<SearchResponse>(`/api/search?${params}`);
    },
    enabled: query.trim().length > 0,
  });

export const usePracticeItems = (certificationId: string | undefined) =>
  useQuery({
    queryKey: keys.practiceItems(certificationId ?? ""),
    queryFn: () => api<PracticeItemView[]>(`/api/practice/items?certificationId=${certificationId}`),
    enabled: !!certificationId,
  });

export function useCreatePracticeItem() {
  const client = useQueryClient();
  return useMutation({
    mutationFn: (request: CreatePracticeItemRequest) => api<PracticeItemView>("/api/practice/items", "POST", request),
    onSuccess: (item) => client.invalidateQueries({ queryKey: keys.practiceItems(item.certificationId) }),
  });
}

export function useSetPracticeItemStatus() {
  const client = useQueryClient();
  return useMutation({
    mutationFn: ({ id, status }: { id: string; status: PracticeItemStatus }) =>
      api<PracticeItemView>(`/api/practice/items/${id}/status`, "PUT", { status }),
    onSuccess: (item) => client.invalidateQueries({ queryKey: keys.practiceItems(item.certificationId) }),
  });
}

export function useDeletePracticeItem(certificationId: string) {
  const client = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => api<void>(`/api/practice/items/${id}`, "DELETE"),
    onSuccess: () => client.invalidateQueries({ queryKey: keys.practiceItems(certificationId) }),
  });
}

const isActive = (run: GenerationJobView) => run.status === "Queued" || run.status === "Running";

/** Recent generation runs, polled while one is in progress so its new items show up as they are written. */
export function useGenerations(certificationId: string) {
  const client = useQueryClient();
  return useQuery({
    queryKey: keys.generations(certificationId),
    queryFn: async () => {
      const runs = await api<GenerationJobView[]>(`/api/practice/generations?certificationId=${certificationId}`);
      if (runs.some(isActive) || runs[0]?.finishedAt) void client.invalidateQueries({ queryKey: keys.practiceItems(certificationId) });
      return runs;
    },
    refetchInterval: (query) => (query.state.data?.some(isActive) ? 2000 : false),
  });
}

export function useStartGeneration() {
  const client = useQueryClient();
  return useMutation({
    mutationFn: (request: GenerateItemsRequest) => api<GenerationJobView>("/api/practice/generations", "POST", request),
    onSuccess: (run) => client.invalidateQueries({ queryKey: keys.generations(run.certificationId) }),
  });
}

export function useCancelGeneration() {
  const client = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => api<GenerationJobView>(`/api/practice/generations/${id}/cancel`, "POST"),
    onSuccess: (run) => client.invalidateQueries({ queryKey: keys.generations(run.certificationId) }),
  });
}
