<script setup lang="ts">
// 選擇附件（送件、補件共用）。父元件用 v-model 拿到選好的 File[]，送出時交給 API。
// 不能用 v-model 綁 <input type="file">：檔案輸入框的值是唯讀的，只能監聽 change
import { ATTACHMENT_ACCEPT, ATTACHMENT_HINT, validateAttachments } from '@/utils/attachments'
import { formatFileSize } from '@/utils/format'
import { computed, ref, useId, watch } from 'vue'

const files = defineModel<File[]>({ required: true })

const props = defineProps<{
  // 送出後後端回傳的附件錯誤（errors.Attachments），和前端的檢查顯示在同一個位置
  serverError?: string
  // 送出中：不能再增減檔案，否則看不出到底送了哪些
  disabled?: boolean
}>()

// 同一頁可能有好幾個這個元件，id 不能寫死
const inputId = useId()
const hintId = useId()
const errorId = useId()

const localError = ref<string | null>(null)
// 後端的錯誤是針對「送出時」的那份清單：使用者增減檔案之後就過時了（例如已經移除了被擋下的檔案），先隱藏。
// 父元件在送出前會清掉 serverError、失敗時再設定，值有變化就重新顯示
const serverErrorShown = ref(props.serverError ?? null)
watch(
  () => props.serverError,
  (value) => {
    serverErrorShown.value = value ?? null
  },
)
const errorMessage = computed(() => localError.value ?? serverErrorShown.value)

// 使用者增減了檔案：自己的錯誤由這次的檢查結果決定，後端的錯誤已經過時
function setLocalError(error: string | null) {
  localError.value = error
  serverErrorShown.value = null
}

function handleChange(event: Event) {
  const input = event.target as HTMLInputElement
  if (!input.files || input.files.length === 0) {
    return
  }

  // 每選一次，input 就會取代上一次選的檔案，所以加進自己的清單。
  // 檢查的是加進去之後的整份清單：分兩次各選 3 個，也要擋下來
  const next = [...files.value, ...Array.from(input.files)]
  // 清空才能再選同一個檔案（值沒變的話不會觸發 change）
  input.value = ''

  const error = validateAttachments(next)
  setLocalError(error)
  // 有錯誤時這一批整個不加入：使用者看到錯誤後重選就好
  if (error === null) {
    files.value = next
  }
}

function removeFile(target: number) {
  files.value = files.value.filter((_, index) => index !== target)
  setLocalError(null)
}
</script>

<template>
  <div class="attachment-picker">
    <label :for="inputId">附件（選填）</label>
    <p :id="hintId" class="hint">{{ ATTACHMENT_HINT }}</p>
    <input
      :id="inputId"
      type="file"
      multiple
      :accept="ATTACHMENT_ACCEPT"
      :disabled="disabled"
      :aria-invalid="!!errorMessage"
      :aria-describedby="`${hintId} ${errorId}`"
      @change="handleChange"
    />
    <p v-if="errorMessage" :id="errorId" class="error" role="alert">{{ errorMessage }}</p>

    <!-- key 用 index：File 沒有唯一的 Id，<li> 也沒有輸入框之類的內部狀態，移除時畫面會正確更新 -->
    <ul v-if="files.length > 0">
      <li v-for="(file, index) in files" :key="index">
        <span>{{ file.name }}（{{ formatFileSize(file.size) }}）</span>
        <button
          type="button"
          :disabled="disabled"
          :aria-label="`移除 ${file.name}`"
          @click="removeFile(index)"
        >
          移除
        </button>
      </li>
    </ul>
  </div>
</template>

<style scoped>
.attachment-picker {
  display: grid;
  gap: 0.25rem;
}
.hint {
  margin: 0;
  color: #555;
}
.error {
  color: #c00;
  margin: 0;
}
ul {
  margin: 0;
  padding-left: 1.25rem;
}
li {
  display: flex;
  gap: 0.5rem;
  align-items: center;
}
</style>
