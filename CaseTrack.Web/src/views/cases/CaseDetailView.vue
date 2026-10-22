<script setup lang="ts">
import { ApiError } from '@/api/client'
import { closeCase, getCase, rejectCase, requestSupplement, reviewCase } from '@/api/cases'
import type { CaseAction, CaseDetails } from '@/api/types'
import ActionWithReason from '@/components/ActionWithReason.vue'
import SupplementHistory from '@/components/SupplementHistory.vue'
import { useDepartments } from '@/composables/useDepartments'
import { caseStatusLabels } from '@/utils/caseStatus'
import { formatDateTime } from '@/utils/format'
import { ref, watch } from 'vue'

// 承辦端的案件詳情：承辦人、主管、分派人員。陳情人資料一律遮罩，
// 能做哪些動作由後端的 allowedActions 決定，前端不自己判斷角色和狀態
const props = defineProps<{ id: string }>()

const { departmentName } = useDepartments()

const caseDetail = ref<CaseDetails | null>(null)
const loading = ref(false)
const isNotFound = ref(false)
const errorMessage = ref<string | null>(null)

// 處理結果顯示在動作元件「外面」：處理完會重新載入，allowedActions 變了，
// 那個動作的元件可能整個消失（例如結案後就沒有結案按鈕），放在元件裡的訊息會跟著不見
const actionSuccess = ref<string | null>(null)
const actionNotice = ref<string | null>(null)
// 必須宣告在下面的 watch 之前：watch 用了 immediate，setup 執行到它時就會讀這兩個 ref

async function load() {
  loading.value = true
  isNotFound.value = false
  errorMessage.value = null
  try {
    caseDetail.value = await getCase(props.id)
  } catch (e) {
    // 換到另一件案件卻載入失敗時，不能繼續顯示上一件的內容
    caseDetail.value = null
    if (e instanceof ApiError && e.status === 404) {
      // 別的單位的案件後端也回 404（跟不存在一樣，不透露案件存在）
      isNotFound.value = true
    } else {
      errorMessage.value = e instanceof Error ? e.message : '載入案件時發生錯誤'
    }
  } finally {
    loading.value = false
  }
}

// 用 watch 不用 onMounted：只換 id 時 Vue Router 會重複使用這個元件。
// 換案件時也清掉上一件的處理結果
watch(
  () => props.id,
  () => {
    actionSuccess.value = null
    actionNotice.value = null
    load()
  },
  { immediate: true },
)

function orNotProvided(value: string | null): string {
  return value ?? '未提供'
}

// ── 處理動作 ──────────────────────────────────────────

// 前端唯一的判斷：後端給的 allowedActions 裡有沒有。角色和狀態的規則都在後端
function can(action: CaseAction): boolean {
  return caseDetail.value?.allowedActions.includes(action) ?? false
}

// 所有動作共用：成功就重新載入（狀態、allowedActions、補件紀錄都由後端說了算，前端不自己改）。
// 409 = 別人剛處理過，畫面上的 allowedActions 過時了：重新載入並在外面提示，不往外拋。
// 其他錯誤（例如 400 欄位錯誤）往外拋，由還在畫面上的動作元件顯示
async function perform(action: () => Promise<void>, successMessage: string) {
  actionSuccess.value = null
  actionNotice.value = null
  try {
    await action()
  } catch (e) {
    if (e instanceof ApiError && e.status === 409) {
      actionNotice.value = '這件案件的狀態已經改變（可能有人剛處理過），畫面已更新為最新狀態'
      await load()
      return
    }
    throw e
  }
  actionSuccess.value = successMessage
  await load()
}

// 受理不需要填寫，直接送出。先確認一次：受理後就進入審核，不能退回「已送件」
const reviewing = ref(false)
async function handleReview() {
  if (reviewing.value || !window.confirm('確定要受理這件案件嗎？')) {
    return
  }
  reviewing.value = true
  try {
    await perform(() => reviewCase(props.id), '已受理')
  } catch (e) {
    actionNotice.value = e instanceof Error ? e.message : '受理時發生錯誤'
  } finally {
    reviewing.value = false
  }
}
</script>

<template>
  <p><RouterLink :to="{ name: 'cases' }">← 回到案件列表</RouterLink></p>

  <!-- 重新載入時（例如處理完之後）不清掉畫面，只有第一次載入才顯示「載入中」 -->
  <p v-if="loading && !caseDetail">載入中…</p>
  <p v-else-if="isNotFound">找不到這件案件。</p>
  <p v-else-if="errorMessage" role="alert">{{ errorMessage }}</p>

  <template v-else-if="caseDetail">
    <h1>{{ caseDetail.subject }}</h1>

    <section>
      <dl>
        <dt>案件編號</dt>
        <dd>{{ caseDetail.caseNumber }}</dd>
        <dt>狀態</dt>
        <dd>{{ caseStatusLabels[caseDetail.status] }}</dd>
        <dt>承辦單位</dt>
        <dd>{{ departmentName(caseDetail.departmentId) }}</dd>
        <dt>送件時間</dt>
        <dd>{{ formatDateTime(caseDetail.submittedAt) }}</dd>
      </dl>
    </section>

    <section>
      <h2>陳情內容</h2>
      <!-- pre-wrap 保留換行；不要把 \n 換成 <br> 再用 v-html（民眾輸入的 HTML 會在承辦人的畫面上被執行） -->
      <p class="multiline">{{ caseDetail.content }}</p>
    </section>

    <section v-if="caseDetail.closingReply">
      <h2>結案回覆</h2>
      <p class="multiline">{{ caseDetail.closingReply }}</p>
    </section>

    <section v-if="caseDetail.rejectionReason">
      <h2>退件／不予受理原因</h2>
      <p class="multiline">{{ caseDetail.rejectionReason }}</p>
    </section>

    <SupplementHistory :requests="caseDetail.supplementRequests" />

    <section>
      <!-- 標明已遮罩：承辦人才知道看到的不是完整資料（查看完整資料的按鈕在之後的時段加上） -->
      <h2>陳情人資料（已遮罩）</h2>
      <dl>
        <dt>姓名</dt>
        <dd>{{ orNotProvided(caseDetail.petitioner.name) }}</dd>
        <dt>電話</dt>
        <dd>{{ orNotProvided(caseDetail.petitioner.phone) }}</dd>
        <dt>Email</dt>
        <dd>{{ orNotProvided(caseDetail.petitioner.email) }}</dd>
        <dt>地址</dt>
        <dd>{{ orNotProvided(caseDetail.petitioner.address) }}</dd>
      </dl>
    </section>

    <section v-if="caseDetail.allowedActions.length > 0 || actionSuccess || actionNotice">
      <h2>處理</h2>
      <!-- 結果訊息放在動作元件外面：處理完重新載入後，元件可能已經不在畫面上 -->
      <p v-if="actionSuccess" role="status">{{ actionSuccess }}</p>
      <p v-if="actionNotice" class="error" role="alert">{{ actionNotice }}</p>

      <div class="actions">
        <button v-if="can('Review')" type="button" :disabled="reviewing" @click="handleReview">
          {{ reviewing ? '受理中…' : '受理' }}
        </button>
        <ActionWithReason
          v-if="can('RequestSupplement')"
          label="要求補件"
          field-label="補件原因（民眾會看到）"
          :maxlength="500"
          :submit="(reason: string) => perform(() => requestSupplement(id, reason), '已要求補件')"
        />
        <ActionWithReason
          v-if="can('Close')"
          label="結案"
          field-label="結案回覆（民眾會看到）"
          :maxlength="2000"
          :submit="(reply: string) => perform(() => closeCase(id, reply), '已結案')"
        />
        <ActionWithReason
          v-if="can('Reject')"
          label="退件"
          field-label="退件原因（民眾會看到）"
          :maxlength="500"
          :submit="(reason: string) => perform(() => rejectCase(id, reason), '已退件')"
        />
      </div>
    </section>
  </template>
</template>

<style scoped>
section {
  margin-top: 1.5rem;
}
dl {
  display: grid;
  grid-template-columns: 6rem 1fr;
  gap: 0.4rem 1rem;
  margin: 0;
}
dt {
  color: #555;
}
dd {
  margin: 0;
}
.multiline {
  white-space: pre-wrap;
}
.actions {
  display: grid;
  gap: 1rem;
  justify-items: start;
}
.error {
  color: #c00;
}
</style>
