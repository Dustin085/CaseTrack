<script setup lang="ts">
import { ApiError } from '@/api/client'
import { downloadCaseList, downloadStatistics } from '@/api/reports'
import { CASE_STATUSES, type CaseStatus } from '@/api/types'
import { caseStatusLabels } from '@/utils/caseStatus'
import { ref } from 'vue'

// 報表匯出：選期間（送件日期），下載統計表或案件清單（Excel）。
// 期間、筆數的上限由後端檢查（400 的訊息直接顯示），前端只擋「沒選日期」這種明顯的情況

// <input type="date"> 的值是 yyyy-MM-dd。用瀏覽器的本地日期（台灣使用者就是台灣的日期），
// 不用 toISOString()：那是 UTC，台灣早上 8 點前會變成前一天
function toDateValue(date: Date): string {
  const month = String(date.getMonth() + 1).padStart(2, '0')
  const day = String(date.getDate()).padStart(2, '0')
  return `${date.getFullYear()}-${month}-${day}`
}

// 預設：這個月 1 號到今天（最常用的「月報」）
const today = new Date()
const from = ref(toDateValue(new Date(today.getFullYear(), today.getMonth(), 1)))
const to = ref(toDateValue(today))
const status = ref<CaseStatus | ''>('')

const downloading = ref<'statistics' | 'case-list' | null>(null)
const errorMessage = ref<string | null>(null)

async function download(kind: 'statistics' | 'case-list') {
  if (downloading.value) {
    return
  }
  if (!from.value || !to.value) {
    errorMessage.value = '請選擇開始與結束日期'
    return
  }
  errorMessage.value = null
  downloading.value = kind
  try {
    if (kind === 'statistics') {
      await downloadStatistics(from.value, to.value)
    } else {
      await downloadCaseList(from.value, to.value, status.value || undefined)
    }
  } catch (e) {
    // 400：期間不對、超過筆數上限。errors 的訊息比 title 具體，取第一個
    const fieldError =
      e instanceof ApiError ? Object.values(e.problem?.errors ?? {}).flat()[0] : undefined
    errorMessage.value = fieldError ?? (e instanceof Error ? e.message : '下載報表時發生錯誤')
  } finally {
    downloading.value = null
  }
}
</script>

<template>
  <h1>報表</h1>

  <section class="card">
    <h2>期間（送件日期）</h2>
    <div class="period">
      <label for="report-from">開始</label>
      <input id="report-from" v-model="from" type="date" :max="to" />
      <label for="report-to">結束</label>
      <input id="report-to" v-model="to" type="date" :min="from" />
    </div>
    <p class="muted">只包含您有權限查看的案件（承辦人、主管為本單位）。最長一年。</p>
    <p v-if="errorMessage" class="message error" role="alert">{{ errorMessage }}</p>
  </section>

  <section class="card">
    <h2>案件統計表</h2>
    <p class="muted">各單位、各狀態的件數與合計。</p>
    <button type="button" :disabled="downloading !== null" @click="download('statistics')">
      {{ downloading === 'statistics' ? '產生中…' : '下載統計表（Excel）' }}
    </button>
  </section>

  <section class="card">
    <h2>案件清單</h2>
    <!-- 告訴使用者為什麼沒有陳情人：匯出的檔案會離開系統，依個資最小化原則不放個資 -->
    <p class="muted">逐筆列出案件編號、主旨、狀態、承辦單位、送件時間。不含陳情人資料。</p>
    <div class="period">
      <label for="report-status">狀態</label>
      <select id="report-status" v-model="status">
        <option value="">全部</option>
        <option v-for="s in CASE_STATUSES" :key="s" :value="s">{{ caseStatusLabels[s] }}</option>
      </select>
    </div>
    <button type="button" :disabled="downloading !== null" @click="download('case-list')">
      {{ downloading === 'case-list' ? '產生中…' : '下載案件清單（Excel）' }}
    </button>
  </section>
</template>

<style scoped>
.period {
  display: flex;
  flex-wrap: wrap;
  gap: 0.5rem;
  align-items: center;
  margin-bottom: 0.75rem;
}
</style>
