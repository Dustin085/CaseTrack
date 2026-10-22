<script setup lang="ts">
import { listCases } from '@/api/cases'
import { CASE_STATUSES, isCaseStatus, type CaseSummary, type PagedResult } from '@/api/types'
import { useDepartments } from '@/composables/useDepartments'
import { caseStatusLabels } from '@/utils/caseStatus'
import StatusBadge from '@/components/StatusBadge.vue'
import { formatDateTime } from '@/utils/format'
import { parsePositiveInt } from '@/utils/query'
import { computed, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'

// 承辦端的案件列表。看得到哪些案件由後端決定（承辦人、主管只有本單位，分派人員全部），前端不再過濾。
// 每頁固定 20 筆：從網址讀的話，?pageSize=500 會超過後端的上限 100，回 400
const PAGE_SIZE = 20

const route = useRoute()
const router = useRouter()

const result = ref<PagedResult<CaseSummary> | null>(null)
const loading = ref(false)
const errorMessage = ref<string | null>(null)

// 頁碼、篩選條件都放在網址上，網址是唯一的來源，用 computed 從網址算出來（跟民眾的列表同樣的理由）。
// 網址是使用者能改的輸入：status 不是合法的狀態（?status=abc）就當成「全部」
const page = computed(() => parsePositiveInt(route.query.page, 1))
const status = computed(() => (isCaseStatus(route.query.status) ? route.query.status : undefined))

// 下拉選單用 v-model 綁定可寫入的 computed：讀的時候從網址來，寫的時候寫回網址。
// 換篩選條件要回到第 1 頁：在第 3 頁換成只有 1 頁的條件，會停在不存在的頁碼。
// status 設成 undefined 時 Vue Router 會把它從網址拿掉（選「全部」）
const selectedStatus = computed({
  get: () => status.value ?? '',
  set: (value: string) => {
    router.push({ query: { ...route.query, page: '1', status: value || undefined } })
  },
})

const totalPages = computed(() =>
  result.value ? Math.ceil(result.value.totalCount / result.value.pageSize) : 0,
)
const hasPreviousPage = computed(() => page.value > 1)
const hasNextPage = computed(() => page.value < totalPages.value)

// 單位名稱：列表只有 departmentId，由共用的 useDepartments 對照
const { departmentName } = useDepartments()

async function load() {
  loading.value = true
  errorMessage.value = null
  try {
    result.value = await listCases({ page: page.value, pageSize: PAGE_SIZE, status: status.value })
    // 網址的頁碼超過最後一頁：換到最後一頁。用 replace，不在瀏覽紀錄留下不存在的頁碼
    if (totalPages.value > 0 && page.value > totalPages.value) {
      await goToPage(totalPages.value, true)
    }
  } catch (e) {
    // 401 不用在這裡處理：apiFetch 會登出，App.vue 監看登入狀態後導向登入頁
    errorMessage.value = e instanceof Error ? e.message : '載入案件時發生錯誤'
  } finally {
    loading.value = false
  }
}

// 換頁只改網址，page 會跟著變，再由下面的 watch 載入
function goToPage(target: number, replace = false) {
  const location = { query: { ...route.query, page: String(target) } }
  return replace ? router.replace(location) : router.push(location)
}

// 頁碼或篩選條件改變都重新載入；immediate：進入頁面時就執行一次
watch([page, status], load, { immediate: true })
</script>

<template>
  <h1>案件列表</h1>

  <div class="filters">
    <label for="status-filter">狀態</label>
    <select id="status-filter" v-model="selectedStatus">
      <option value="">全部</option>
      <option v-for="s in CASE_STATUSES" :key="s" :value="s">{{ caseStatusLabels[s] }}</option>
    </select>
  </div>

  <p v-if="errorMessage" role="alert">{{ errorMessage }}</p>

  <p v-if="loading">載入中…</p>
  <p v-else-if="result?.totalCount === 0">沒有符合條件的案件。</p>
  <template v-else-if="result">
    <table>
      <thead>
        <tr>
          <th>案件編號</th>
          <th>主旨</th>
          <th>狀態</th>
          <th>承辦單位</th>
          <th>送件時間</th>
        </tr>
      </thead>
      <tbody>
        <tr v-for="item in result.items" :key="item.id">
          <td>
            <RouterLink :to="{ name: 'case-detail', params: { id: item.id } }">
              {{ item.caseNumber }}
            </RouterLink>
          </td>
          <td>{{ item.subject }}</td>
          <td><StatusBadge :status="item.status" /></td>
          <td>{{ departmentName(item.departmentId) }}</td>
          <td>{{ formatDateTime(item.submittedAt) }}</td>
        </tr>
      </tbody>
    </table>

    <nav class="pager">
      <button type="button" :disabled="!hasPreviousPage" @click="goToPage(page - 1)">上一頁</button>
      <span>第 {{ page }} / {{ totalPages }} 頁，共 {{ result.totalCount }} 件</span>
      <button type="button" :disabled="!hasNextPage" @click="goToPage(page + 1)">下一頁</button>
    </nav>
  </template>
</template>

<style scoped>
.filters {
  display: flex;
  gap: 0.5rem;
  align-items: center;
  margin-bottom: 1rem;
}
.pager {
  display: flex;
  gap: 1rem;
  align-items: center;
  margin-top: 1rem;
}
</style>
