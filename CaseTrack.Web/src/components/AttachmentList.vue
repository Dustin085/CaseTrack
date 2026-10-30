<script setup lang="ts">
// 附件清單與下載（民眾端、承辦端共用；案件的附件與每一筆補件的附件都用這個元件）
import { apiDownload } from '@/api/client'
import type { AttachmentDetails } from '@/api/types'
import { formatDateTime, formatFileSize } from '@/utils/format'
import { ref } from 'vue'

const props = defineProps<{
  attachments: AttachmentDetails[]
  // 民眾端與承辦端的下載網址不同（/api/my/cases、/api/cases），由父元件決定，
  // 這個元件不知道網址長什麼樣子：後端改路由時只要改父元件
  downloadPath: (attachment: AttachmentDetails) => string
}>()

// 正在下載的附件：每個按鈕各自停用，下載一個時其他的還能按
const downloadingIds = ref<string[]>([])
const errorMessage = ref<string | null>(null)

async function handleDownload(attachment: AttachmentDetails) {
  // 真正擋住重複下載的是這一行（按鈕的 disabled 要等下一輪畫面更新才生效）
  if (downloadingIds.value.includes(attachment.id)) {
    return
  }
  errorMessage.value = null
  downloadingIds.value = [...downloadingIds.value, attachment.id]
  try {
    await apiDownload(props.downloadPath(attachment), attachment.fileName)
  } catch (e) {
    // ApiError（404、5xx）與 NetworkError 的訊息都是中文，直接顯示
    errorMessage.value = e instanceof Error ? e.message : '下載檔案時發生錯誤'
  } finally {
    // 放在 finally：下載失敗時也要恢復按鈕，否則要重新整理頁面才能再按
    downloadingIds.value = downloadingIds.value.filter((id) => id !== attachment.id)
  }
}
</script>

<template>
  <p v-if="attachments.length === 0">沒有附件</p>
  <template v-else>
    <p v-if="errorMessage" class="error" role="alert">{{ errorMessage }}</p>
    <!-- 不顯示 contentType：民眾看不懂 application/pdf，檔名本身就有副檔名 -->
    <table>
      <thead>
        <tr>
          <!-- th 加上 scope：螢幕報讀念到「1.2 MB」時，才會說出這是「大小」 -->
          <th scope="col">檔名</th>
          <th scope="col">大小</th>
          <th scope="col">上傳時間</th>
          <th scope="col"><span class="visually-hidden">操作</span></th>
        </tr>
      </thead>
      <tbody>
        <tr v-for="attachment in attachments" :key="attachment.id">
          <td>{{ attachment.fileName }}</td>
          <td>{{ formatFileSize(attachment.size) }}</td>
          <td>{{ formatDateTime(attachment.uploadedAt) }}</td>
          <td>
            <!-- aria-label 帶檔名：只寫「下載」的話，螢幕報讀用 Tab 跳過來時分不出是哪一個檔案 -->
            <button
              type="button"
              :disabled="downloadingIds.includes(attachment.id)"
              :aria-label="`下載 ${attachment.fileName}`"
              @click="handleDownload(attachment)"
            >
              {{ downloadingIds.includes(attachment.id) ? '下載中…' : '下載' }}
            </button>
          </td>
        </tr>
      </tbody>
    </table>
  </template>
</template>

<style scoped>
table {
  border-collapse: collapse;
}
th,
td {
  padding: 0.3rem 0.75rem 0.3rem 0.75rem;
  text-align: left;
}
th {
  color: #555;
  font-weight: normal;
}
.error {
  color: #c00;
  margin: 0;
}
</style>
