<script setup lang="ts">
import { ApiError } from '@/api/client'
import { getMyCase, submitSupplement } from '@/api/myCases'
import type { CitizenCaseDetails } from '@/api/types'
import StatusBadge from '@/components/StatusBadge.vue'
import SupplementHistory from '@/components/SupplementHistory.vue'
import { formatDateTime } from '@/utils/format'
import { computed, ref, watch } from 'vue'

// 民眾查看自己的案件；狀態是「補件中」時可以補件。
// 路由參數 id 由 router 的 props: true 傳進來
const props = defineProps<{ id: string }>()

const caseDetail = ref<CitizenCaseDetails | null>(null)
const loading = ref(false)
const isNotFound = ref(false)
const errorMessage = ref<string | null>(null)

async function load() {
  loading.value = true
  isNotFound.value = false
  errorMessage.value = null
  try {
    caseDetail.value = await getMyCase(props.id)
  } catch (e) {
    // 換到另一件案件卻載入失敗時，不能繼續顯示上一件的內容
    caseDetail.value = null
    if (e instanceof ApiError && e.status === 404) {
      // 別人的案件後端也回 404（跟不存在一樣，不透露案件存在），所以畫面上不區分這兩種情況
      isNotFound.value = true
    } else {
      errorMessage.value = e instanceof Error ? e.message : '載入案件時發生錯誤'
    }
  } finally {
    loading.value = false
  }
}

// 用 watch 不用 onMounted：只換 id 時 Vue Router 會重複使用這個元件，onMounted 不會再執行
watch(() => props.id, load, { immediate: true })

// ── 補件 ──────────────────────────────────────────────

// 這次要補的：還沒補件的那一筆（同一時間最多一筆，由後端的 Domain 保證）
const pendingSupplement = computed(
  () => caseDetail.value?.supplementRequests.find((r) => r.submittedAt === null) ?? null,
)
const canSubmitSupplement = computed(
  () => caseDetail.value?.status === 'AwaitingSupplement' && pendingSupplement.value !== null,
)

const supplementContent = ref('')
// 補件說明的錯誤：顯示在表單裡的欄位旁邊
const supplementError = ref<string | null>(null)
// 送出後的結果（成功、409）：顯示在表單「外面」。送出後會重新載入，狀態不再是補件中，
// 表單整個被隱藏，放在表單裡的訊息會跟著消失，民眾只看到表單突然不見
const supplementSuccess = ref(false)
const supplementConflict = ref<string | null>(null)
const submittingSupplement = ref(false)

// 換到另一件案件時，清掉上一件的補件狀態
watch(
  () => props.id,
  () => {
    supplementContent.value = ''
    supplementError.value = null
    supplementSuccess.value = false
    supplementConflict.value = null
  },
)

async function handleSubmitSupplement() {
  // 真正擋住重複送出的是這一行（按鈕的 disabled 要等下一輪畫面更新才生效）
  if (submittingSupplement.value) {
    return
  }
  supplementError.value = null
  supplementSuccess.value = false
  supplementConflict.value = null
  submittingSupplement.value = true
  try {
    // 附件的選擇元件還沒接上，先送空的清單
    await submitSupplement(props.id, supplementContent.value.trim(), [])
    supplementContent.value = ''
    supplementSuccess.value = true
    // 重新載入，不自己改 status：狀態轉換的規則在後端的 Domain，前端自己改等於再寫一次業務規則
    await load()
  } catch (e) {
    if (e instanceof ApiError && e.problem?.errors) {
      supplementError.value = e.problem.errors['Content']?.[0] ?? '請確認補件說明'
    } else if (e instanceof ApiError && e.status === 409) {
      // 狀態已經不是「補件中」：例如逾期被自動退件、在另一個分頁補過了。重新載入讓畫面跟著更新
      supplementConflict.value =
        '這件案件目前無法補件（可能已經補過或已逾期），畫面已更新為最新狀態'
      await load()
    } else {
      supplementError.value = e instanceof Error ? e.message : '送出補件時發生錯誤'
    }
  } finally {
    submittingSupplement.value = false
  }
}

function orNotProvided(value: string | null): string {
  return value ?? '未提供'
}
</script>

<template>
  <p><RouterLink :to="{ name: 'my-cases' }">← 回到我的案件</RouterLink></p>

  <!-- 重新載入時（例如補件後）不清掉畫面，只有第一次載入才顯示「載入中」 -->
  <p v-if="loading && !caseDetail">載入中…</p>
  <p v-else-if="isNotFound">找不到這件案件。</p>
  <p v-else-if="errorMessage" role="alert">{{ errorMessage }}</p>

  <template v-else-if="caseDetail">
    <h1>{{ caseDetail.subject }}</h1>

    <!-- 一件案件是「一筆資料、很多欄位」，用定義清單不用表格：螢幕報讀會念成「主旨：…」 -->
    <section>
      <dl>
        <dt>案件編號</dt>
        <dd>{{ caseDetail.caseNumber }}</dd>
        <dt>狀態</dt>
        <dd><StatusBadge :status="caseDetail.status" /></dd>
        <dt>送件時間</dt>
        <dd>{{ formatDateTime(caseDetail.submittedAt) }}</dd>
      </dl>
    </section>

    <section>
      <h2>陳情內容</h2>
      <!-- pre-wrap 保留民眾輸入的換行。不要把 \n 換成 <br> 再用 v-html：那會讓民眾輸入的 HTML 被執行（XSS） -->
      <p class="multiline">{{ caseDetail.content }}</p>
    </section>

    <section v-if="caseDetail.closingReply">
      <h2>結案回覆</h2>
      <p class="multiline">{{ caseDetail.closingReply }}</p>
    </section>

    <!-- 退件和不予受理都存在 rejectionReason，民眾看到的統一叫「未受理原因」 -->
    <section v-if="caseDetail.rejectionReason">
      <h2>未受理原因</h2>
      <p class="multiline">{{ caseDetail.rejectionReason }}</p>
    </section>

    <section v-if="canSubmitSupplement && pendingSupplement" class="supplement-form">
      <h2>補件</h2>
      <p>承辦人於 {{ formatDateTime(pendingSupplement.requestedAt) }} 要求補件，原因：</p>
      <p class="multiline">{{ pendingSupplement.reason }}</p>

      <form novalidate @submit.prevent="handleSubmitSupplement">
        <label for="supplement-content">補件說明</label>
        <textarea
          id="supplement-content"
          v-model="supplementContent"
          rows="6"
          maxlength="2000"
          :aria-invalid="!!supplementError"
          aria-describedby="supplement-error"
        ></textarea>
        <p v-if="supplementError" id="supplement-error" class="error" role="alert">
          {{ supplementError }}
        </p>
        <button type="submit" :disabled="submittingSupplement">
          {{ submittingSupplement ? '送出中…' : '送出補件' }}
        </button>
      </form>
    </section>
    <!-- 放在補件表單外面：送出後表單會隱藏，這些訊息要留下來 -->
    <p v-if="supplementSuccess" role="status">已送出補件，承辦人會繼續處理。</p>
    <p v-if="supplementConflict" class="error" role="alert">{{ supplementConflict }}</p>

    <SupplementHistory :requests="caseDetail.supplementRequests" />

    <section>
      <h2>陳情人資料</h2>
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
.supplement-form form {
  display: grid;
  gap: 0.5rem;
  max-width: 640px;
}
.supplement-form textarea {
  font: inherit;
  padding: 0.4rem;
}
.supplement-form button {
  justify-self: start;
  padding: 0.5rem 1.5rem;
}
.error {
  color: #c00;
  margin: 0;
}
</style>
