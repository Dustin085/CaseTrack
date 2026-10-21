<script setup lang="ts">
import { listMyCases } from '@/api/myCases'
import type { CaseSummary, PagedResult } from '@/api/types'
import { caseStatusLabels } from '@/utils/caseStatus'
import { formatDateTime } from '@/utils/format'
import { parsePositiveInt } from '@/utils/query'
import { computed, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'

// 民眾不需要調整每頁筆數，固定 20 筆：少一個從網址讀的參數，就少一個要檢查的輸入
const PAGE_SIZE = 20

const route = useRoute()
const router = useRouter()

// 這些狀態寫在 <script setup> 裡：每個頁面各有一份（跟 useAuth 寫在函式外面、全 app 共用一份相反）
const result = ref<PagedResult<CaseSummary> | null>(null)
const loading = ref(false)
const errorMessage = ref<string | null>(null)

// 頁碼放在網址上（?page=2）：重新整理、瀏覽器的上一頁、分享網址都會停在同一頁。
// 網址是唯一的來源，page 用 computed 從網址算出來，不另外存一份 ref：
// 只換查詢字串時 Vue Router 會重複使用這個元件（onMounted 不會再執行），
// 另外存一份的話，按瀏覽器的上一頁，網址變了，ref 卻還是舊的頁碼
const page = computed(() => parsePositiveInt(route.query.page, 1))

const totalPages = computed(() =>
  result.value ? Math.ceil(result.value.totalCount / result.value.pageSize) : 0,
)
const hasPreviousPage = computed(() => page.value > 1)
const hasNextPage = computed(() => page.value < totalPages.value)

async function load() {
  loading.value = true
  errorMessage.value = null
  try {
    result.value = await listMyCases(page.value, PAGE_SIZE)
    // 網址的頁碼超過最後一頁（例如 ?page=99 但只有 2 頁）：換到最後一頁，不要顯示空的表格。
    // 用 replace 不用 push：不在瀏覽紀錄留下一個不存在的頁碼
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

// immediate：進入頁面時就執行一次，等於 onMounted(load)，之後頁碼每次改變都重新載入
watch(page, load, { immediate: true })
</script>

<template>
  <h1>我的案件</h1>

  <p v-if="errorMessage" role="alert">{{ errorMessage }}</p>

  <p v-if="loading">載入中…</p>
  <p v-else-if="result?.totalCount === 0">
    還沒有陳情案件。<RouterLink :to="{ name: 'submit-case' }">我要陳情</RouterLink>
  </p>
  <template v-else-if="result">
    <table>
      <thead>
        <tr>
          <th>案件編號</th>
          <th>主旨</th>
          <th>狀態</th>
          <th>送件時間</th>
        </tr>
      </thead>
      <tbody>
        <tr v-for="item in result.items" :key="item.id">
          <td>
            <RouterLink :to="{ name: 'my-case-detail', params: { id: item.id } }">
              {{ item.caseNumber }}
            </RouterLink>
          </td>
          <td>{{ item.subject }}</td>
          <td>{{ caseStatusLabels[item.status] }}</td>
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
table {
  width: 100%;
  border-collapse: collapse;
}
th,
td {
  padding: 0.5rem;
  border-bottom: 1px solid #ddd;
  text-align: left;
}
.pager {
  display: flex;
  gap: 1rem;
  align-items: center;
  margin-top: 1rem;
}
</style>
