import { apiFetch } from '@/api/client'
import type {
  CitizenCaseDetails,
  CaseSummary,
  PagedResult,
  SubmitCaseRequest,
  SubmitCaseResult,
} from '@/api/types'

// 民眾端的 API（/api/my/cases）。頁面呼叫這些函式，不直接寫網址：
// - 網址只寫在這裡，後端改路由只改這個檔案
// - apiFetch<T> 的 T 只是承諾、不是檢查，集中在這裡寫一次，頁面不會各自寫錯
// - 網址一定要 "/" 開頭：沒有的話是相對於目前頁面的路徑（在 /my/cases/123 會變成 /my/cases/api/...），
//   不會經過 proxy，Vite 回傳 index.html（200），response.json() 才失敗，錯誤訊息完全看不出原因

// page 從 1 開始。用 URLSearchParams 組查詢字串：之後加上關鍵字時，中文和特殊字元會自動編碼
export function listMyCases(page: number, pageSize: number): Promise<PagedResult<CaseSummary>> {
  const searchParams = new URLSearchParams({
    page: page.toString(),
    pageSize: pageSize.toString(),
  })

  return apiFetch<PagedResult<CaseSummary>>(`/api/my/cases?${searchParams}`)
}

export function getMyCase(id: string): Promise<CitizenCaseDetails> {
  return apiFetch<CitizenCaseDetails>(`/api/my/cases/${id}`)
}

export function submitCase(request: SubmitCaseRequest): Promise<SubmitCaseResult> {
  return apiFetch<SubmitCaseResult>('/api/my/cases', {
    method: 'POST',
    body: request,
  })
}

export function submitSupplement(id: string, content: string): Promise<void> {
  return apiFetch<void>(`/api/my/cases/${id}/supplement-submission`, {
    method: 'POST',
    body: { content },
  })
}
