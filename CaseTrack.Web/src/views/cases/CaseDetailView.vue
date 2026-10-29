<script setup lang="ts">
import { ApiError } from '@/api/client'
import {
  assignCase,
  attachmentPath,
  closeCase,
  dismissCase,
  getCase,
  getPetitioner,
  rejectCase,
  requestSupplement,
  reviewCase,
} from '@/api/cases'
import type { AttachmentDetails, CaseAction, CaseDetails, PetitionerDetails } from '@/api/types'
import ActionWithReason from '@/components/ActionWithReason.vue'
import AttachmentList from '@/components/AttachmentList.vue'
import StatusBadge from '@/components/StatusBadge.vue'
import SupplementHistory from '@/components/SupplementHistory.vue'
import { useDepartments } from '@/composables/useDepartments'
import { formatDateTime } from '@/utils/format'
import { computed, ref, watch } from 'vue'

// 承辦端的案件詳情：承辦人、主管、分派人員。陳情人資料一律遮罩，
// 能做哪些動作由後端的 allowedActions 決定，前端不自己判斷角色和狀態
const props = defineProps<{ id: string }>()

const { departments, departmentName } = useDepartments()

// 交給案件的附件與補件紀錄的 AttachmentList（定義一次，不在模板裡寫兩次箭頭函式）
function downloadPath(attachment: AttachmentDetails): string {
  return attachmentPath(props.id, attachment.id)
}

const caseDetail = ref<CaseDetails | null>(null)
const loading = ref(false)
const isNotFound = ref(false)
const errorMessage = ref<string | null>(null)
// 完整的陳情人資料（每次取得都會留下查詢紀錄）。只存在這個頁面的 ref：
// 不放 sessionStorage 或全 app 共用的狀態，離開頁面就消失
const fullPetitioner = ref<PetitionerDetails | null>(null)

// 處理結果顯示在動作元件「外面」：處理完會重新載入，allowedActions 變了，
// 那個動作的元件可能整個消失（例如結案後就沒有結案按鈕），放在元件裡的訊息會跟著不見
const actionSuccess = ref<string | null>(null)
const actionNotice = ref<string | null>(null)
// 必須宣告在下面的 watch 之前：watch 用了 immediate，setup 執行到它時就會讀這兩個 ref

async function load() {
  // 記下這次載入的是哪一件：換案件時前一件的回應可能晚到，晚到的結果不能蓋掉新的案件
  const requestedId = props.id
  loading.value = true
  isNotFound.value = false
  errorMessage.value = null
  // 每次重新載入都回到遮罩：換案件時不能留著上一件的個資；處理完之後也不再需要完整資料
  fullPetitioner.value = null
  try {
    const result = await getCase(requestedId)
    if (props.id !== requestedId) {
      return
    }
    caseDetail.value = result
  } catch (e) {
    if (props.id !== requestedId) {
      return
    }
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

// 分派（分派人員）：選單位、送出。只有這裡用到，所以直接寫在頁面裡，不另外抽成元件。
// 已送件的案件可以改派：預設選目前的單位，改派時不用重新找
const selectedDepartmentId = ref('')
const assigning = ref(false)
watch(caseDetail, (detail) => {
  selectedDepartmentId.value = detail?.departmentId ?? ''
})

async function handleAssign() {
  // 還沒選單位就不能送出（按鈕也是停用的，這裡再擋一次）
  if (assigning.value || selectedDepartmentId.value === '') {
    return
  }
  assigning.value = true
  try {
    const name = departmentName(selectedDepartmentId.value)
    await perform(() => assignCase(props.id, selectedDepartmentId.value), `已分派到${name}`)
  } catch (e) {
    actionNotice.value = e instanceof Error ? e.message : '分派時發生錯誤'
  } finally {
    assigning.value = false
  }
}

// ── 完整的陳情人資料 ──────────────────────────────────

const gettingPetitioner = ref(false)
const petitionerError = ref<string | null>(null)

async function handleShowFullPetitioner() {
  // 連點兩下會寫兩筆查詢紀錄，紀錄就不準了
  if (gettingPetitioner.value) {
    return
  }
  // 記下是哪一件：回應回來之前換到別的案件，A 案件的個資不能顯示在 B 案件的頁面上（等於個資外洩）
  const requestedId = props.id
  gettingPetitioner.value = true
  petitionerError.value = null
  try {
    const result = await getPetitioner(requestedId)
    if (props.id === requestedId) {
      fullPetitioner.value = result
    }
  } catch (e) {
    petitionerError.value = e instanceof Error ? e.message : '取得完整資料時發生錯誤'
  } finally {
    gettingPetitioner.value = false
  }
}

// 講完電話就隱藏：完整個資留在螢幕上的時間越短越好
function hideFullPetitioner() {
  fullPetitioner.value = null
}

// 遮罩和完整的版本共用同一段畫面
const shownPetitioner = computed(() => fullPetitioner.value ?? caseDetail.value?.petitioner ?? null)
</script>

<template>
  <p class="back"><RouterLink :to="{ name: 'cases' }">← 回到案件列表</RouterLink></p>

  <!-- 重新載入時（例如處理完之後）不清掉畫面，只有第一次載入才顯示「載入中」 -->
  <p v-if="loading && !caseDetail">載入中…</p>
  <p v-else-if="isNotFound" class="card">找不到這件案件。</p>
  <p v-else-if="errorMessage" class="message error" role="alert">{{ errorMessage }}</p>

  <template v-else-if="caseDetail">
    <header class="page-header">
      <div>
        <p class="muted">{{ caseDetail.caseNumber }}</p>
        <h1>{{ caseDetail.subject }}</h1>
      </div>
      <StatusBadge :status="caseDetail.status" />
    </header>

    <section class="card">
      <dl>
        <dt>承辦單位</dt>
        <dd>{{ departmentName(caseDetail.departmentId) }}</dd>
        <dt>送件時間</dt>
        <dd>{{ formatDateTime(caseDetail.submittedAt) }}</dd>
      </dl>
    </section>

    <section class="card">
      <h2>陳情內容</h2>
      <!-- pre-wrap 保留換行；不要把 \n 換成 <br> 再用 v-html（民眾輸入的 HTML 會在承辦人的畫面上被執行） -->
      <p class="multiline">{{ caseDetail.content }}</p>
    </section>

    <section class="card">
      <h2>附件</h2>
      <AttachmentList :attachments="caseDetail.attachments" :download-path="downloadPath" />
    </section>

    <section v-if="caseDetail.closingReply" class="card">
      <h2>結案回覆</h2>
      <p class="multiline">{{ caseDetail.closingReply }}</p>
    </section>

    <section v-if="caseDetail.rejectionReason" class="card">
      <h2>退件／不予受理原因</h2>
      <p class="multiline">{{ caseDetail.rejectionReason }}</p>
    </section>

    <SupplementHistory
      class="card"
      :requests="caseDetail.supplementRequests"
      :download-path="downloadPath"
    />

    <!-- 顯示完整個資時整張卡片換成醒目的顏色：提醒承辦人畫面上有完整的個資 -->
    <section v-if="shownPetitioner" class="card petitioner" :class="{ revealed: fullPetitioner }">
      <div class="petitioner-header">
        <h2>陳情人資料</h2>
        <span class="tag">{{ fullPetitioner ? '完整資料' : '已遮罩' }}</span>
      </div>
      <dl>
        <dt>姓名</dt>
        <dd>{{ orNotProvided(shownPetitioner.name) }}</dd>
        <dt>電話</dt>
        <dd>{{ orNotProvided(shownPetitioner.phone) }}</dd>
        <dt>Email</dt>
        <dd>{{ orNotProvided(shownPetitioner.email) }}</dd>
        <dt>地址</dt>
        <dd>{{ orNotProvided(shownPetitioner.address) }}</dd>
      </dl>

      <div v-if="can('ViewPetitioner')" class="petitioner-actions">
        <template v-if="!fullPetitioner">
          <button type="button" :disabled="gettingPetitioner" @click="handleShowFullPetitioner">
            {{ gettingPetitioner ? '取得中…' : '查看完整資料' }}
          </button>
          <!-- 讓承辦人知道這個動作會被稽核：告知，也是一種嚇阻 -->
          <span class="muted">查看會留下紀錄（查看人、時間、案件）</span>
        </template>
        <template v-else>
          <button type="button" @click="hideFullPetitioner">隱藏</button>
          <span class="muted">聯絡完畢請隱藏</span>
        </template>
      </div>
      <p v-if="petitionerError" class="message error" role="alert">{{ petitionerError }}</p>
    </section>

    <section
      v-if="caseDetail.allowedActions.length > 0 || actionSuccess || actionNotice"
      class="card"
    >
      <h2>處理</h2>
      <!-- 結果訊息放在動作元件外面：處理完重新載入後，元件可能已經不在畫面上 -->
      <p v-if="actionSuccess" class="message success" role="status">{{ actionSuccess }}</p>
      <p v-if="actionNotice" class="message error" role="alert">{{ actionNotice }}</p>

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

        <form v-if="can('Assign')" class="assign" @submit.prevent="handleAssign">
          <label for="assign-department">分派到</label>
          <select id="assign-department" v-model="selectedDepartmentId">
            <option value="" disabled>請選擇單位</option>
            <option v-for="d in departments" :key="d.id" :value="d.id">{{ d.name }}</option>
          </select>
          <button type="submit" :disabled="assigning || selectedDepartmentId === ''">
            {{ assigning ? '分派中…' : caseDetail.departmentId ? '改派' : '分派' }}
          </button>
        </form>
        <ActionWithReason
          v-if="can('Dismiss')"
          label="不予受理"
          field-label="不予受理原因（民眾會看到）"
          :maxlength="500"
          :submit="(reason: string) => perform(() => dismissCase(id, reason), '已不予受理')"
        />
      </div>
    </section>
  </template>
</template>

<style scoped>
.back {
  margin: 0 0 1rem;
}
.page-header {
  display: flex;
  justify-content: space-between;
  align-items: flex-start;
  gap: 1rem;
}
.page-header p,
.page-header h1 {
  margin: 0;
}
dl {
  display: grid;
  grid-template-columns: 6rem 1fr;
  gap: 0.4rem 1rem;
  margin: 0;
}
dt {
  color: var(--color-muted);
}
dd {
  margin: 0;
}
.multiline {
  white-space: pre-wrap;
  margin: 0;
}
.petitioner-header {
  display: flex;
  align-items: center;
  gap: 0.75rem;
  margin-bottom: 0.75rem;
}
.petitioner-header h2 {
  margin: 0;
}
.tag {
  font-size: 0.8rem;
  padding: 0.05rem 0.5rem;
  border-radius: 999px;
  background: #eceef1;
  color: var(--color-muted);
}
.petitioner.revealed {
  border: 2px solid var(--color-warning-border);
  background: var(--color-warning-bg);
}
.petitioner.revealed .tag {
  background: var(--color-warning-border);
  color: #3b2a00;
}
.petitioner-actions {
  display: flex;
  align-items: center;
  gap: 0.75rem;
  margin-top: 1rem;
}
.actions {
  display: grid;
  gap: 1rem;
  justify-items: start;
}
.assign {
  display: flex;
  gap: 0.5rem;
  align-items: center;
}
</style>
