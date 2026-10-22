// 後端 API 的合約（JSON 的形狀）。對照 CaseTrack.Api 和 CaseTrack.Application 的 record：
// 後端改了欄位名稱，這裡要跟著改，用到的地方就會編譯失敗，不會等到執行時才發現。
// Guid、DateTimeOffset 在 JSON 裡都是字串

// 狀態用名稱輸出（後端的 JsonStringEnumConverter）
export const CASE_STATUSES = [
  'Submitted',
  'UnderReview',
  'AwaitingSupplement',
  'Closed',
  'Rejected',
] as const
export type CaseStatus = (typeof CASE_STATUSES)[number]

export function isCaseStatus(value: unknown): value is CaseStatus {
  return typeof value === 'string' && (CASE_STATUSES as readonly string[]).includes(value)
}

// 承辦端可以做的動作（後端的 CaseAction enum，用名稱輸出）。GET /api/cases/{id} 的 allowedActions
// 列出目前的使用者「現在」能做哪些，前端照著顯示按鈕，不自己判斷角色和狀態
export const CASE_ACTIONS = [
  'Review',
  'RequestSupplement',
  'Close',
  'Reject',
  'ViewPetitioner',
  'Assign',
  'Dismiss',
] as const
export type CaseAction = (typeof CASE_ACTIONS)[number]

export enum Roles {
  CaseOfficer = 'CaseOfficer',
  Supervisor = 'Supervisor',
  Dispatcher = 'Dispatcher',
  Administrator = 'Administrator',
  Citizen = 'Citizen',
}

// ── 身分 ────────────────────────────────────────────────

// POST /dev/token（只有開發環境）
export interface DevTokenRequest {
  userId: string
  name: string
  roles: string[]
  departmentId: string | null
}

export interface DevTokenResponse {
  accessToken: string
  expiresAt: string
}

// GET /api/me
export interface Me {
  userId: string
  name: string | null
  roles: string[]
  departmentId: string | null
}

// GET /api/departments
export interface Department {
  id: string
  code: string
  name: string
}

// ── 承辦端 /api/cases ─────────────────────────────────

// GET /api/cases/{id}：承辦端看到的案件。陳情人一律遮罩，完整資料另外呼叫 /petitioner（會留下查詢紀錄）
export interface CaseDetails {
  id: string
  caseNumber: string
  subject: string
  content: string
  status: CaseStatus
  submittedAt: string
  rejectionReason: string | null
  closingReply: string | null
  petitioner: PetitionerDetails // 遮罩過的（王*明）
  supplementRequests: SupplementRequestDetails[]
  departmentId: string | null
  allowedActions: CaseAction[]
}

// ── 民眾端 /api/my/cases ─────────────────────────────────

export interface PetitionerInput {
  name: string
  phone: string | null
  email: string | null
  address: string | null
}

// POST /api/my/cases
export interface SubmitCaseRequest {
  subject: string
  content: string
  petitioner: PetitionerInput
}

export interface SubmitCaseResult {
  id: string
  caseNumber: string
}

// GET /api/my/cases、GET /api/cases 的每一筆
export interface CaseSummary {
  id: string
  caseNumber: string
  subject: string
  status: CaseStatus
  submittedAt: string
  departmentId: string | null
}

export interface PagedResult<T> {
  items: T[]
  page: number
  pageSize: number
  totalCount: number
}

export interface PetitionerDetails {
  name: string | null
  phone: string | null
  email: string | null
  address: string | null
}

export interface SupplementRequestDetails {
  sequence: number
  reason: string
  requestedAt: string
  submittedAt: string | null
  submittedContent: string | null
}

// GET /api/my/cases/{id}：民眾自己的案件，陳情人資料不遮罩
export interface CitizenCaseDetails {
  id: string
  caseNumber: string
  subject: string
  content: string
  status: CaseStatus
  submittedAt: string
  rejectionReason: string | null
  closingReply: string | null
  petitioner: PetitionerDetails
  supplementRequests: SupplementRequestDetails[]
}

// ── 錯誤 ────────────────────────────────────────────────

// 錯誤回應（RFC 9457，Content-Type: application/problem+json）。
// errors 只有驗證失敗（400）才有：欄位名稱 → 錯誤訊息，例如 { "Subject": ["主旨不可為空"] }。
// 其他自訂欄位（例如 409 的 currentStatus）也會攤平在最外層
export interface ProblemDetails {
  type?: string
  title?: string
  status?: number
  detail?: string
  errors?: Record<string, string[]>
  [extension: string]: unknown
}
