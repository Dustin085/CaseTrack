<script setup lang="ts">
import { listAuditEntries, type AuditEntryFilter } from '@/api/admin'
import { ApiError } from '@/api/client'
import type { AuditEntrySummary, PagedResult } from '@/api/types'
import { formatDateTime } from '@/utils/format'
import { computed, onMounted, reactive, ref } from 'vue'

// 稽核紀錄查詢（管理者）：誰、什麼時候、改了哪筆資料的哪些欄位。
// 案件內容、個資的欄位後端不給值（hidden），這裡只列出欄位名稱和「（內容不公開）」

const PAGE_SIZE = 50

// 中文名稱只是顯示用；查詢時送出的是後端的類型名稱
const entityTypeLabels: Record<string, string> = {
  Case: '案件',
  SupplementRequest: '補件',
  Attachment: '附件',
  Department: '單位',
}
const actionLabels: Record<string, string> = { Added: '新增', Modified: '修改', Deleted: '刪除' }

// filter 是輸入框的值；applied 是目前結果用的條件（換頁時用它，輸入框改了但還沒按查詢，不能影響換頁）
const filter = reactive<AuditEntryFilter>({
  entityType: '',
  entityId: '',
  changedBy: '',
  from: '',
  to: '',
})
let applied: AuditEntryFilter = { ...filter }

const page = ref(1)
const result = ref<PagedResult<AuditEntrySummary> | null>(null)
const loading = ref(false)
const errorMessage = ref<string | null>(null)

const totalPages = computed(() =>
  result.value ? Math.ceil(result.value.totalCount / result.value.pageSize) : 0,
)

async function load(targetPage: number) {
  loading.value = true
  errorMessage.value = null
  try {
    result.value = await listAuditEntries(applied, targetPage, PAGE_SIZE)
    page.value = targetPage
  } catch (e) {
    const fieldError =
      e instanceof ApiError ? Object.values(e.problem?.errors ?? {}).flat()[0] : undefined
    errorMessage.value = fieldError ?? (e instanceof Error ? e.message : '查詢稽核紀錄時發生錯誤')
  } finally {
    loading.value = false
  }
}

function search() {
  applied = { ...filter }
  load(1)
}

onMounted(() => load(1))
</script>

<template>
  <h1>稽核紀錄</h1>

  <section class="card">
    <form class="filters" role="search" @submit.prevent="search">
      <div class="field">
        <label for="audit-entity-type">資料類型</label>
        <select id="audit-entity-type" v-model="filter.entityType">
          <option value="">全部</option>
          <option v-for="(label, type) in entityTypeLabels" :key="type" :value="type">
            {{ label }}
          </option>
        </select>
      </div>
      <div class="field">
        <label for="audit-entity-id">資料 Id</label>
        <input id="audit-entity-id" v-model="filter.entityId" maxlength="50" />
      </div>
      <div class="field">
        <label for="audit-changed-by">異動者</label>
        <input id="audit-changed-by" v-model="filter.changedBy" maxlength="100" />
      </div>
      <div class="field">
        <label for="audit-from">開始日期</label>
        <input id="audit-from" v-model="filter.from" type="date" :max="filter.to || undefined" />
      </div>
      <div class="field">
        <label for="audit-to">結束日期</label>
        <input id="audit-to" v-model="filter.to" type="date" :min="filter.from || undefined" />
      </div>
      <button type="submit" :disabled="loading">查詢</button>
    </form>
    <p class="muted">案件內容與個人資料的欄位只列出欄位名稱，不顯示內容。</p>
  </section>

  <p v-if="errorMessage" class="message error" role="alert">{{ errorMessage }}</p>
  <p v-else-if="result === null">載入中…</p>
  <p v-else-if="result.items.length === 0">沒有符合條件的紀錄</p>
  <template v-else>
    <table>
      <thead>
        <tr>
          <th scope="col">時間</th>
          <th scope="col">資料</th>
          <th scope="col">動作</th>
          <th scope="col">異動者</th>
          <th scope="col">欄位</th>
        </tr>
      </thead>
      <tbody>
        <tr v-for="entry in result.items" :key="entry.id">
          <td class="nowrap">{{ formatDateTime(entry.changedAt) }}</td>
          <td>
            {{ entityTypeLabels[entry.entityType] ?? entry.entityType }}
            <div class="muted id">{{ entry.entityId }}</div>
          </td>
          <td>{{ actionLabels[entry.action] ?? entry.action }}</td>
          <td class="id">{{ entry.changedBy }}</td>
          <td>
            <ul class="changes">
              <li v-for="change in entry.changes" :key="change.field">
                <strong>{{ change.field }}</strong>
                <span v-if="change.hidden" class="muted">（內容不公開）</span>
                <template v-else>
                  <template v-if="entry.action === 'Modified'">
                    {{ change.old ?? '（空）' }} → {{ change.new ?? '（空）' }}
                  </template>
                  <template v-else>{{ change.new ?? change.old ?? '（空）' }}</template>
                </template>
              </li>
            </ul>
          </td>
        </tr>
      </tbody>
    </table>
    <nav class="pager" aria-label="分頁">
      <button type="button" :disabled="loading || page <= 1" @click="load(page - 1)">上一頁</button>
      <span>第 {{ page }} / {{ totalPages }} 頁，共 {{ result.totalCount }} 筆</span>
      <button type="button" :disabled="loading || page >= totalPages" @click="load(page + 1)">
        下一頁
      </button>
    </nav>
  </template>
</template>

<style scoped>
.filters {
  display: flex;
  flex-wrap: wrap;
  gap: 0.75rem;
  align-items: flex-end;
}
.field {
  display: flex;
  flex-direction: column;
  font-size: 0.9rem;
}
.changes {
  margin: 0;
  padding-left: 1rem;
  font-size: 0.9rem;
}
.changes strong {
  margin-right: 0.5rem;
}
/* Guid 很長，允許在任何地方換行，不要把表格撐破 */
.id {
  word-break: break-all;
  font-size: 0.85rem;
}
.nowrap {
  white-space: nowrap;
}
.pager {
  display: flex;
  gap: 1rem;
  align-items: center;
  justify-content: center;
  margin-top: 1rem;
}
</style>
