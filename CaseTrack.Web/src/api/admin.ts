import { apiFetch } from '@/api/client'
import type { AuditEntrySummary, Department, PagedResult } from '@/api/types'

// 管理者的 API（/api/admin）：單位維護、稽核紀錄查詢。其他角色呼叫會得到 403

// 長度上限與後端 CreateDepartmentRequest 一致
export const MAX_DEPARTMENT_CODE_LENGTH = 10
export const MAX_DEPARTMENT_NAME_LENGTH = 50

export function listAdminDepartments(): Promise<Department[]> {
  return apiFetch<Department[]>('/api/admin/departments')
}

// 代碼後端會轉成大寫；重複時回 400（errors.Code）
export function createDepartment(code: string, name: string): Promise<Department> {
  return apiFetch<Department>('/api/admin/departments', { method: 'POST', body: { code, name } })
}

export function renameDepartment(id: string, name: string): Promise<void> {
  return apiFetch<void>(`/api/admin/departments/${id}/name`, { method: 'PUT', body: { name } })
}

// 停用後不能再分派新案件，已分派的案件不受影響
export function deactivateDepartment(id: string): Promise<void> {
  return apiFetch<void>(`/api/admin/departments/${id}/deactivation`, { method: 'POST' })
}

export function activateDepartment(id: string): Promise<void> {
  return apiFetch<void>(`/api/admin/departments/${id}/activation`, { method: 'POST' })
}

// 條件都可以不填；沒有值的不放進查詢字串（否則是字串 "undefined"）。日期是 yyyy-MM-dd，台灣的日期、含頭含尾
export interface AuditEntryFilter {
  entityType?: string
  entityId?: string
  changedBy?: string
  from?: string
  to?: string
}

export function listAuditEntries(
  filter: AuditEntryFilter,
  page: number,
  pageSize: number,
): Promise<PagedResult<AuditEntrySummary>> {
  const searchParams = new URLSearchParams({ page: String(page), pageSize: String(pageSize) })
  for (const [key, value] of Object.entries(filter)) {
    const trimmed = value?.trim()
    if (trimmed) {
      searchParams.set(key, trimmed)
    }
  }
  return apiFetch<PagedResult<AuditEntrySummary>>(`/api/admin/audit-entries?${searchParams}`)
}
