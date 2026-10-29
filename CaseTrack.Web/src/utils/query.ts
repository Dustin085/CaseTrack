import type { LocationQueryValue } from 'vue-router'

// 從網址的查詢字串讀出正整數（頁碼等）。網址是使用者可以任意修改的輸入：
// "abc" → NaN、"0"、"-1"、"1.5"、?page=1&page=2（陣列）都不合法，一律改用預設值，
// 不然就送出 ?page=NaN，後端回 400
export function parsePositiveInt(
  value: LocationQueryValue | LocationQueryValue[] | undefined,
  fallback: number,
): number {
  if (typeof value !== 'string') {
    return fallback
  }
  const parsed = Number(value)
  return Number.isInteger(parsed) && parsed >= 1 ? parsed : fallback
}

// 從網址讀出搜尋的文字。陣列（?keyword=a&keyword=b）、空白都當成沒有；
// 超過 maxLength 就截斷，不然後端回 400，整個列表變成錯誤訊息
export function parseSearchText(
  value: LocationQueryValue | LocationQueryValue[] | undefined,
  maxLength: number,
): string | undefined {
  if (typeof value !== 'string') {
    return undefined
  }
  const trimmed = value.trim().slice(0, maxLength)
  return trimmed === '' ? undefined : trimmed
}
