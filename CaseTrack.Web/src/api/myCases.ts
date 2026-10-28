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
// 送件、補件是 multipart 表單（要附檔案），不是 JSON：欄位名稱對應 C# 的屬性路徑（巢狀用 "."），
// 和後端驗證錯誤的 errors 的 key 一樣

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

export function submitCase(request: SubmitCaseRequest, files: File[]): Promise<SubmitCaseResult> {
  const formData = new FormData()
  formData.append('Subject', request.subject)
  formData.append('Content', request.content)
  formData.append('Petitioner.Name', request.petitioner.name)
  appendIfPresent(formData, 'Petitioner.Phone', request.petitioner.phone)
  appendIfPresent(formData, 'Petitioner.Email', request.petitioner.email)
  appendIfPresent(formData, 'Petitioner.Address', request.petitioner.address)
  appendFiles(formData, files)

  return apiFetch<SubmitCaseResult>('/api/my/cases', {
    method: 'POST',
    body: formData,
  })
}

export function submitSupplement(id: string, content: string, files: File[]): Promise<void> {
  const formData = new FormData()
  formData.append('Content', content)
  appendFiles(formData, files)

  return apiFetch<void>(`/api/my/cases/${id}/supplement-submission`, {
    method: 'POST',
    body: formData,
  })
}

// 表單沒有 null：append(name, null) 會送出字串 "null"。沒填就不送這個欄位，後端綁定成 null
function appendIfPresent(formData: FormData, name: string, value: string | null) {
  if (value !== null) {
    formData.append(name, value)
  }
}

// 同一個欄位名稱 append 好幾次，後端收成 List<IFormFile>；檔名由瀏覽器從 File.name 帶上。
// 一定要用 for...of（元素）：for...in 跑的是索引 "0"、"1"，會變成文字欄位，
// 型別檢查和後端都不會報錯，送件成功但附件全部消失
function appendFiles(formData: FormData, files: File[]) {
  for (const file of files) {
    formData.append('Attachments', file)
  }
}
