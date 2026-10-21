import { apiFetch } from '@/api/client'
import type { CaseStatus, PagedResult, CaseSummary, Department } from '@/api/types'

// 承辦端的 API（/api/cases），給承辦人、主管、分派人員用。民眾呼叫會得到 403。
// 看得到哪些案件由後端決定：承辦人、主管只有本單位，分派人員全部

// status 沒有值時不放進查詢字串：URLSearchParams 會把 undefined 變成字串 "undefined"，後端回 400
export function listCases(query: {
  page: number
  pageSize: number
  status?: CaseStatus
}): Promise<PagedResult<CaseSummary>> {
  const searchParams = new URLSearchParams({
    page: query.page.toString(),
    pageSize: query.pageSize.toString(),
  })
  if (query.status) {
    searchParams.set('status', query.status)
  }

  return apiFetch<PagedResult<CaseSummary>>(`/api/cases?${searchParams}`)
}

// 單位清單（登入就能看）：列表只有 departmentId，用它對照單位名稱
export function listDepartments(): Promise<Department[]> {
  return apiFetch<Department[]>('/api/departments')
}
