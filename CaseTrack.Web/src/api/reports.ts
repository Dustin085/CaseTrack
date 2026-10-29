import { apiDownload } from '@/api/client'
import type { CaseStatus } from '@/api/types'

// 報表匯出（/api/reports），承辦人、主管、分派人員。內容只有自己看得到的案件（承辦人、主管是本單位）。
// 日期是 yyyy-MM-dd（<input type="date"> 的值），台灣的日期、含頭含尾。
// 檔名由前端組（apiDownload 不解析 Content-Disposition），格式跟後端一樣

export function downloadStatistics(from: string, to: string): Promise<void> {
  const searchParams = new URLSearchParams({ from, to })
  return apiDownload(
    `/api/reports/statistics?${searchParams}`,
    `案件統計_${compact(from)}-${compact(to)}.xlsx`,
  )
}

// status 沒有值時不放進查詢字串（否則是字串 "undefined"，後端回 400）
export function downloadCaseList(from: string, to: string, status?: CaseStatus): Promise<void> {
  const searchParams = new URLSearchParams({ from, to })
  if (status) {
    searchParams.set('status', status)
  }
  return apiDownload(
    `/api/reports/case-list?${searchParams}`,
    `案件清單_${compact(from)}-${compact(to)}.xlsx`,
  )
}

// 2026-10-01 → 20261001
function compact(date: string): string {
  return date.replaceAll('-', '')
}
