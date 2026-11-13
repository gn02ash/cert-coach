import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  api,
  type AuthProvider,
  type CertificationDetail,
  type CertificationSummary,
  type AiCapabilities,
  type AiAllowanceView,
  type DashboardView,
  type DocumentSummary,
  type OutlineDraft,
  type OutlineImport,
  type CreatePracticeItemRequest,
  type PracticeItemStatus,
  type UpdatePracticeItemRequest,
  type PracticeItemView,
  type GenerateItemsRequest,
  type GenerationJobView,
  type AnswerRequest,
  type AnswerResult,
  type ReviewSet,
  type MockExamSummary,
  type MockExamView,
  type StartMockExamRequest,
  type ReadinessReport,
  type ActivityView,
  type AdviceView,
  type ApiTokenView,
  type CreateApiTokenRequest,
  type CreatedApiToken,
  type ResourceSuggestions,
  type ResourceView,
  type SaveResourceRequest,
  type ProfileDraft,
  type ProfileView,
  type RecommendationsView,
  type SaveProfileRequest,
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
  aiAllowance: ["ai-allowance"] as const,
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

export function useUpdatePracticeItem() {
  const client = useQueryClient();
  return useMutation({
    mutationFn: ({ id, request }: { id: string; request: UpdatePracticeItemRequest }) =>
      api<PracticeItemView>(`/api/practice/items/${id}`, "PUT", request),
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

/**
 * Today's review queue, or a quiz on some objectives. Fetched once per visit: refetching mid-session would
 * reshuffle the cards the learner is working through.
 */
export const useReviewSet = (scope: { certificationId?: string; objectiveIds?: string[]; domainId?: string; weak?: boolean }) =>
  useQuery({
    queryKey: ["review", scope.certificationId ?? "", scope.domainId ?? "", !!scope.weak, ...(scope.objectiveIds ?? [])],
    queryFn: () => {
      if (scope.objectiveIds?.length) {
        return api<ReviewSet>(`/api/review/quiz?${scope.objectiveIds.map((id) => `objectiveIds=${id}`).join("&")}`);
      }

      if (scope.certificationId && (scope.weak || scope.domainId)) {
        const query = new URLSearchParams({ certificationId: scope.certificationId });
        if (scope.domainId) query.set("domainId", scope.domainId);
        if (scope.weak) query.set("weak", "true");
        return api<ReviewSet>(`/api/review/practice?${query}`);
      }

      return api<ReviewSet>(`/api/review${scope.certificationId ? `?certificationId=${scope.certificationId}` : ""}`);
    },
    // Not kept between visits: a second round of practice should draw again.
    gcTime: 0,
    staleTime: Infinity,
    refetchOnWindowFocus: false,
  });

export function useAnswer() {
  const client = useQueryClient();
  return useMutation({
    mutationFn: ({ id, request }: { id: string; request: AnswerRequest }) =>
      api<AnswerResult>(`/api/review/items/${id}/answer`, "POST", request),
    onSuccess: () => {
      void client.invalidateQueries({ queryKey: keys.dashboard });
      void client.invalidateQueries({ queryKey: ["readiness"] });
    },
  });
}

export const useMockExams = (certificationId: string | undefined) =>
  useQuery({
    queryKey: ["mock-exams", certificationId ?? ""],
    queryFn: () => api<MockExamSummary[]>(`/api/mock-exams?certificationId=${certificationId}`),
    enabled: !!certificationId,
  });

export const useMockExam = (id: string | undefined) =>
  useQuery({
    queryKey: ["mock-exam", id ?? ""],
    queryFn: () => api<MockExamView>(`/api/mock-exams/${id}`),
    enabled: !!id,
    // The timer runs locally from the server's remaining time; refetching mid-exam would reset the selection state.
    refetchOnWindowFocus: false,
  });

export function useStartMockExam() {
  return useMutation({ mutationFn: (request: StartMockExamRequest) => api<MockExamView>("/api/mock-exams", "POST", request) });
}

export function useSaveMockAnswer(examId: string) {
  return useMutation({
    mutationFn: ({ itemId, selected }: { itemId: string; selected: number[] }) =>
      api<void>(`/api/mock-exams/${examId}/answers/${itemId}`, "PUT", { selected }),
  });
}

export function useSubmitMockExam() {
  const client = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => api<MockExamView>(`/api/mock-exams/${id}/submit`, "POST"),
    onSuccess: (exam) => {
      client.setQueryData(["mock-exam", exam.id], exam);
      void client.invalidateQueries({ queryKey: ["mock-exams"] });
      void client.invalidateQueries({ queryKey: ["readiness"] });
      void client.invalidateQueries({ queryKey: keys.dashboard });
    },
  });
}

export const useReadiness = (certificationId: string) =>
  useQuery({
    queryKey: ["readiness", certificationId],
    queryFn: () => api<ReadinessReport>(`/api/readiness/${certificationId}`),
  });

/** Keyed under the dashboard, so anything that refreshes the dashboard (a session done, an answer) refreshes it too. */
export const useActivity = () =>
  useQuery({ queryKey: [...keys.dashboard, "activity"], queryFn: () => api<ActivityView>("/api/activity") });

const careerKeys = { profile: ["career", "profile"] as const, recommendations: ["career", "recommendations"] as const };

export const useCareerProfile = () =>
  useQuery({ queryKey: careerKeys.profile, queryFn: () => api<ProfileView>("/api/career/profile") });

export function useSaveCareerProfile() {
  const client = useQueryClient();
  return useMutation({
    mutationFn: (request: SaveProfileRequest) => api<ProfileView>("/api/career/profile", "PUT", request),
    onSuccess: (profile) => {
      client.setQueryData(careerKeys.profile, profile);
      void client.invalidateQueries({ queryKey: careerKeys.recommendations });
    },
  });
}

export function useDeleteCareerProfile() {
  const client = useQueryClient();
  return useMutation({
    mutationFn: () => api<void>("/api/career/profile", "DELETE"),
    onSuccess: () => {
      client.removeQueries({ queryKey: careerKeys.recommendations });
      return client.invalidateQueries({ queryKey: careerKeys.profile });
    },
  });
}

/** Reads a CV, pasted or as a PDF, into a draft profile. Nothing is stored. */
export const useReadCv = () =>
  useMutation({
    mutationFn: (cv: { text: string } | { file: File }) => {
      if ("text" in cv) return api<ProfileDraft>("/api/career/cv/text", "POST", cv);
      const form = new FormData();
      form.append("file", cv.file);
      return upload<ProfileDraft>("/api/career/cv/file", form);
    },
  });

export const useRecommendations = () =>
  useQuery({ queryKey: careerKeys.recommendations, queryFn: () => api<RecommendationsView>("/api/career/recommendations") });

export const useAdvice = () =>
  useMutation({ mutationFn: () => api<AdviceView>("/api/career/recommendations/advice", "POST") });

const resourceKeys = {
  saved: (certificationId: string) => ["resources", certificationId] as const,
  suggestions: (objectiveId: string) => ["resource-suggestions", objectiveId] as const,
};

export const useResources = (certificationId: string) =>
  useQuery({
    queryKey: resourceKeys.saved(certificationId),
    queryFn: () => api<ResourceView[]>(`/api/resources?certificationId=${certificationId}`),
    enabled: !!certificationId,
  });

export const useResourceSuggestions = (objectiveId: string) =>
  useQuery({
    queryKey: resourceKeys.suggestions(objectiveId),
    queryFn: () => api<ResourceSuggestions>(`/api/resources/suggestions?objectiveId=${objectiveId}`),
    enabled: !!objectiveId,
    staleTime: 5 * 60 * 1000,
  });

export function useSaveResource() {
  const client = useQueryClient();
  return useMutation({
    mutationFn: (request: SaveResourceRequest) => api<ResourceView>("/api/resources", "POST", request),
    onSuccess: (resource) => {
      void client.invalidateQueries({ queryKey: resourceKeys.saved(resource.certificationId) });
      if (resource.objectiveId) void client.invalidateQueries({ queryKey: resourceKeys.suggestions(resource.objectiveId) });
    },
  });
}

export function useSetResourceDone() {
  const client = useQueryClient();
  return useMutation({
    mutationFn: ({ id, done }: { id: string; done: boolean }) => api<ResourceView>(`/api/resources/${id}/done`, "PUT", { done }),
    // The reply is the saved resource, so the list is updated from it without another request.
    onSuccess: (resource) =>
      client.setQueryData<ResourceView[]>(resourceKeys.saved(resource.certificationId), (list) =>
        list?.map((r) => (r.id === resource.id ? resource : r)),
      ),
  });
}

export function useDeleteResource(certificationId: string) {
  const client = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => api<void>(`/api/resources/${id}`, "DELETE"),
    onSuccess: () => {
      void client.invalidateQueries({ queryKey: resourceKeys.saved(certificationId) });
      void client.invalidateQueries({ queryKey: ["resource-suggestions"] });
    },
  });
}

const tokenKey = ["api-tokens"] as const;

export const useApiTokens = () => useQuery({ queryKey: tokenKey, queryFn: () => api<ApiTokenView[]>("/api/tokens") });

export function useCreateApiToken() {
  const client = useQueryClient();
  return useMutation({
    mutationFn: (request: CreateApiTokenRequest) => api<CreatedApiToken>("/api/tokens", "POST", request),
    onSuccess: () => client.invalidateQueries({ queryKey: tokenKey }),
  });
}

export function useRevokeApiToken() {
  const client = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => api<void>(`/api/tokens/${id}`, "DELETE"),
    onSuccess: () => client.invalidateQueries({ queryKey: tokenKey }),
  });
}

/** Refetched often enough to follow AI use without polling: on focus and after 30 seconds. */
export const useAiAllowance = () =>
  useQuery({
    queryKey: keys.aiAllowance,
    queryFn: () => api<AiAllowanceView>("/api/billing/allowance"),
    staleTime: 30_000,
  });
