import type { CaseStatus } from '@/api/types'

// 案件狀態的中文名稱。型別用 Record<CaseStatus, string>：每一個狀態都必須有名稱，
// 後端新增狀態、types.ts 跟著加上之後，這裡沒補就編譯失敗，畫面不會顯示 undefined
// （跟後端 ApplyScope 的 switch 把每一種範圍都寫出來是同樣的道理）
export const caseStatusLabels: Record<CaseStatus, string> = {
  Submitted: '已送件',
  UnderReview: '審核中',
  AwaitingSupplement: '補件中',
  PendingApproval: '待核定',
  Closed: '已結案',
  Rejected: '已退件',
}
