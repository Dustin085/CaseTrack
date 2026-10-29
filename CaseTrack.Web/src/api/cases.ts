import { apiFetch } from '@/api/client'
import type {
  CaseStatus,
  PagedResult,
  CaseSummary,
  Department,
  CaseDetails,
  PetitionerDetails,
} from '@/api/types'

// 承辦端的 API（/api/cases），給承辦人、主管、分派人員用。民眾呼叫會得到 403。
// 看得到哪些案件由後端決定：承辦人、主管只有本單位，分派人員全部

// 關鍵字的長度上限，與後端 ListCasesRequest.Keyword 的 [MaxLength(100)] 一致
export const MAX_KEYWORD_LENGTH = 100

// status、keyword 沒有值時不放進查詢字串：URLSearchParams 會把 undefined 變成字串 "undefined"
// （status 會回 400，keyword 會變成搜尋 "undefined"）。
// keyword 比對主旨與案件編號（部分符合），中文與特殊字元由 URLSearchParams 編碼
export function listCases(query: {
  page: number
  pageSize: number
  status?: CaseStatus
  keyword?: string
}): Promise<PagedResult<CaseSummary>> {
  const searchParams = new URLSearchParams({
    page: query.page.toString(),
    pageSize: query.pageSize.toString(),
  })
  if (query.status) {
    searchParams.set('status', query.status)
  }
  if (query.keyword) {
    searchParams.set('keyword', query.keyword)
  }

  return apiFetch<PagedResult<CaseSummary>>(`/api/cases?${searchParams}`)
}

export function getCase(id: string): Promise<CaseDetails> {
  return apiFetch<CaseDetails>(`/api/cases/${id}`)
}

// 完整的陳情人資料：每次呼叫都會留下個資查詢紀錄。只在使用者明確要求時呼叫，結果不要另外保存
export function getPetitioner(id: string): Promise<PetitionerDetails> {
  return apiFetch<PetitionerDetails>(`/api/cases/${id}/petitioner`)
}

// 附件的下載網址（送件、補件的附件都是這個網址）。只回傳網址：下載由 AttachmentList 呼叫 apiDownload
export function attachmentPath(caseId: string, attachmentId: string): string {
  return `/api/cases/${caseId}/attachments/${attachmentId}`
}

// 單位清單（登入就能看）：列表只有 departmentId，用它對照單位名稱
export function listDepartments(): Promise<Department[]> {
  return apiFetch<Department[]>('/api/departments')
}

// ── 處理案件的動作 ─────────────────────────────────────
// 成功都回 204（沒有內容），所以是 apiFetch<void>。
// 狀態不允許回 409、沒有權限回 403：畫面上的 allowedActions 過時了（例如別人剛處理過）就會發生

// 受理：沒有 body。不要傳 body: {}，apiFetch 會因此加上 Content-Type 送出一個空物件
export function reviewCase(id: string): Promise<void> {
  return apiFetch<void>(`/api/cases/${id}/review`, { method: 'POST' })
}

export function requestSupplement(id: string, reason: string): Promise<void> {
  return apiFetch<void>(`/api/cases/${id}/supplement-requests`, {
    method: 'POST',
    body: { reason },
  })
}

// 送出結案：不會直接結案，案件變成待核定，主管核定後才結案（民眾才看得到回覆）
export function closeCase(id: string, reply: string): Promise<void> {
  return apiFetch<void>(`/api/cases/${id}/close`, { method: 'POST', body: { reply } })
}

// 主管：核定結案。不能核定自己送出的（409）
export function approveClosing(id: string): Promise<void> {
  return apiFetch<void>(`/api/cases/${id}/closing-approval`, { method: 'POST' })
}

// 主管：退回結案，案件回到審核中，承辦人修改後重新送出
export function returnClosing(id: string, reason: string): Promise<void> {
  return apiFetch<void>(`/api/cases/${id}/closing-return`, { method: 'POST', body: { reason } })
}

// 承辦單位審核後退件
export function rejectCase(id: string, reason: string): Promise<void> {
  return apiFetch<void>(`/api/cases/${id}/reject`, { method: 'POST', body: { reason } })
}

// 分派人員：分派到單位
export function assignCase(id: string, departmentId: string): Promise<void> {
  return apiFetch<void>(`/api/cases/${id}/assignment`, { method: 'POST', body: { departmentId } })
}

// 分派人員：不予受理（只能在分派之前）
export function dismissCase(id: string, reason: string): Promise<void> {
  return apiFetch<void>(`/api/cases/${id}/dismissal`, { method: 'POST', body: { reason } })
}
