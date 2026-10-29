<script setup lang="ts">
// 補件紀錄：民眾和承辦端的案件詳情共用。沒有紀錄時不顯示，呼叫的地方不用自己判斷
import type { AttachmentDetails, SupplementRequestDetails } from '@/api/types'
import AttachmentList from '@/components/AttachmentList.vue'
import { formatDateTime } from '@/utils/format'

defineProps<{
  requests: SupplementRequestDetails[]
  // 只是轉交給 AttachmentList：民眾端與承辦端的下載網址不同，由案件詳情決定
  downloadPath: (attachment: AttachmentDetails) => string
}>()
</script>

<template>
  <section v-if="requests.length > 0">
    <h2>補件紀錄</h2>
    <ol class="supplements">
      <li v-for="request in requests" :key="request.sequence">
        <dl>
          <dt>要求時間</dt>
          <dd>{{ formatDateTime(request.requestedAt) }}</dd>
          <dt>要求原因</dt>
          <dd class="multiline">{{ request.reason }}</dd>
          <dt>補件時間</dt>
          <dd>{{ request.submittedAt ? formatDateTime(request.submittedAt) : '尚未補件' }}</dd>
          <!-- 還沒補件時不顯示附件：顯示「沒有附件」會讓人以為民眾補件時沒附檔案。
               用 submittedAt 判斷「補過了沒」（補件說明必填，兩者同時有值，但意思是「已補件」的是這個） -->
          <template v-if="request.submittedAt">
            <dt>補件說明</dt>
            <dd class="multiline">{{ request.submittedContent }}</dd>
            <dt>補件附件</dt>
            <dd>
              <AttachmentList :attachments="request.attachments" :download-path="downloadPath" />
            </dd>
          </template>
        </dl>
      </li>
    </ol>
  </section>
</template>

<!-- scoped 的樣式只套用在這個元件：父元件的樣式只會套到這裡的根元素（section），
     套不到裡面的 ol、dt、dd，所以用到的樣式都要寫在這裡 -->
<style scoped>
.supplements {
  display: grid;
  gap: 1rem;
  padding-left: 1.5rem;
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
