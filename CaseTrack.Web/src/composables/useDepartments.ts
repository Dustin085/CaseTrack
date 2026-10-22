import { listDepartments } from '@/api/cases'
import type { Department } from '@/api/types'
import { computed, ref } from 'vue'

// 單位清單：列表、詳情顯示單位名稱，分派時的下拉選單。
// 寫在函式外面：全 app 共用一份，單位很少變動，載入一次就好（換頁不用重新載入）
const departments = ref<Department[]>([])
const names = computed(() => new Map(departments.value.map((d) => [d.id, d.name])))

// 進行中的請求：同時有多個元件呼叫 useDepartments()，只送一次
let loading: Promise<void> | null = null

// 不在檔案最外層 await：那樣「什麼時候呼叫 API」就變成「什麼時候被 import」，
// 登入前被 import 會 401（apiFetch 會因此登出）；失敗的話連帶 import 它的頁面也載入失敗，而且永遠不會重試。
// 改成第一次呼叫 useDepartments() 時才載入
function ensureLoaded() {
  if (departments.value.length > 0) {
    return
  }
  loading ??= listDepartments()
    .then((result) => {
      departments.value = result
    })
    .catch(() => {
      // 單位名稱只是輔助資訊：載入失敗時名稱顯示「—」，不影響頁面本身
    })
    .finally(() => {
      // 清掉之後，失敗的話下次呼叫會重試
      loading = null
    })
}

// 「還沒分派」和「找不到名稱」要分開：前者是分派人員最需要的資訊，
// 後者是單位清單還在載入或載入失敗，不能顯示成「沒有單位」
function departmentName(departmentId: string | null): string {
  if (departmentId === null) {
    return '未分派'
  }
  return names.value.get(departmentId) ?? '—'
}

export function useDepartments() {
  ensureLoaded()
  return { departments, departmentName }
}
