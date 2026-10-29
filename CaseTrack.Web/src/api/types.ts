// 後端 API 的合約（JSON 的形狀）。對照 CaseTrack.Api 和 CaseTrack.Application 的 record：
// 後端改了欄位名稱，這裡要跟著改，用到的地方就會編譯失敗，不會等到執行時才發現。
// Guid、DateTimeOffset 在 JSON 裡都是字串

// 狀態用名稱輸出（後端的 JsonStringEnumConverter）
export const CASE_STATUSES = [
  'Submitted',
  'UnderReview',
  'AwaitingSupplement',
  'PendingApproval', // 承辦人送出結案，等主管核定（民眾端看到的是 UnderReview）
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
  'ApproveClosing',
  'ReturnClosing',
  'Assign',
  'Dismiss',
] as const
export type CaseAction = (typeof CASE_ACTIONS)[number]

// 角色（後端 Application 的 Roles 常數）。不用 TypeScript 的 enum：enum 會編譯成真的物件，
// 不是「只有型別、執行時消失」的寫法（erasableSyntaxOnly、Node 直接執行 TS 都不支援）
export const ROLES = [
  'CaseOfficer',
  'Supervisor',
  'Dispatcher',
  'Administrator',
  'Citizen',
] as const
export type Role = (typeof ROLES)[number]

// ── 身分 ────────────────────────────────────────────────

// POST /dev/token（只有開發環境）
export interface DevTokenRequest {
  userId: string
  name: string
  roles: Role[]
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
  // 用 string[] 不用 Role[]：角色來自 IdP，可能有前端不認識的角色（其他系統的），不能假設只有這幾種
  roles: string[]
  departmentId: string | null
}

// GET /api/departments（包含停用的：舊案件要能顯示單位名稱；分派的選單只列啟用中的）
export interface Department {
  id: string
  code: string
  name: string
  isActive: boolean
}

// ── 管理者 /api/admin ─────────────────────────────────

// 稽核紀錄的一個欄位。hidden = true：管理者不能看的欄位（案件內容、個資），後端只告訴你「這個欄位被改了」，值是 null
export interface AuditFieldChange {
  field: string
  old: string | null
  new: string | null
  hidden: boolean
}

// GET /api/admin/audit-entries。action 是 Added、Modified、Deleted
export interface AuditEntrySummary {
  id: number
  entityType: string
  entityId: string
  action: string
  changes: AuditFieldChange[]
  changedBy: string
  changedAt: string
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
  closingReply: string | null // 待核定時是送出的回覆，已結案時是正式的回覆
  closingReturnReason: string | null // 主管最近一次退回結案的原因
  petitioner: PetitionerDetails // 遮罩過的（王*明）
  attachments: AttachmentDetails[]
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

// 附件（只有資料，沒有檔案內容；下載另外呼叫下載的 API）。
// 送件附的放在案件的 attachments，補件附的放在各自 SupplementRequestDetails 的 attachments。
// contentType 是後端用檔頭判斷出來的，不是民眾上傳時瀏覽器給的
export interface AttachmentDetails {
  id: string
  fileName: string
  contentType: string
  size: number
  uploadedAt: string
}

export interface SupplementRequestDetails {
  sequence: number
  reason: string
  requestedAt: string
  submittedAt: string | null
  submittedContent: string | null
  attachments: AttachmentDetails[]
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
  attachments: AttachmentDetails[]
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
