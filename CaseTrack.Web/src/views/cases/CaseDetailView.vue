<script setup lang="ts">
import { ApiError } from '@/api/client'
import { getCase } from '@/api/cases'
import type { CaseDetails } from '@/api/types'
import SupplementHistory from '@/components/SupplementHistory.vue'
import { useDepartments } from '@/composables/useDepartments'
import { caseStatusLabels } from '@/utils/caseStatus'
import { formatDateTime } from '@/utils/format'
import { ref, watch } from 'vue'

// 承辦端的案件詳情：承辦人、主管、分派人員。陳情人資料一律遮罩，
// 能做哪些動作由後端的 allowedActions 決定（處理按鈕在下一個時段加上）
const props = defineProps<{ id: string }>()

const { departmentName } = useDepartments()

const caseDetail = ref<CaseDetails | null>(null)
const loading = ref(false)
const isNotFound = ref(false)
const errorMessage = ref<string | null>(null)

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

// 用 watch 不用 onMounted：只換 id 時 Vue Router 會重複使用這個元件
watch(() => props.id, load, { immediate: true })

function orNotProvided(value: string | null): string {
  return value ?? '未提供'
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
</style>
