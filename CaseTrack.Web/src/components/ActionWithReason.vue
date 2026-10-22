<script setup lang="ts">
// 需要填寫一段文字的處理動作（要求補件、結案、退件、不予受理）：按鈕 → 展開 → 填寫 → 送出。
// 元件只管畫面和送出的狀態；真正呼叫哪個 API 由父元件透過 submit 決定。
// submit 用函式 prop 而不是 emit：元件要 await 結果，才知道該收起來還是顯示錯誤，emit 拿不到回傳值
import { ApiError } from '@/api/client'
import { ref, useId } from 'vue'

const props = defineProps<{
  label: string // 按鈕文字，例如「結案」
  fieldLabel: string // 文字框的標籤，例如「結案回覆」
  maxlength: number // 跟後端的 [MaxLength] 一致
  submit: (text: string) => Promise<void>
}>()

// 同一頁會有好幾個這個元件：id 寫死的話會重複，label 只會對到第一個輸入框
const inputId = useId()
const errorId = useId()

const isOpen = ref(false)
const text = ref('')
const submitting = ref(false)
const errorMessage = ref<string | null>(null)

function toggle() {
  isOpen.value = !isOpen.value
  errorMessage.value = null
}

async function handleSubmit() {
  // 真正擋住重複送出的是這一行（按鈕的 disabled 要等下一輪畫面更新才生效）
  if (submitting.value) {
    return
  }
  submitting.value = true
  errorMessage.value = null
  try {
    await props.submit(text.value.trim())
    text.value = ''
    isOpen.value = false
  } catch (e) {
    if (e instanceof ApiError && e.problem?.errors) {
      // 欄位名稱可能是 Reason 或 Reply，元件不用知道：取第一個欄位的第一個訊息
      errorMessage.value = Object.values(e.problem.errors)[0]?.[0] ?? e.message
    } else {
      errorMessage.value = e instanceof Error ? e.message : '送出時發生錯誤'
    }
  } finally {
    submitting.value = false
  }
}
</script>

<template>
  <div class="action">
    <button type="button" :aria-expanded="isOpen" @click="toggle">{{ label }}</button>

    <form v-if="isOpen" novalidate @submit.prevent="handleSubmit">
      <label :for="inputId">{{ fieldLabel }}</label>
      <textarea
        :id="inputId"
        v-model="text"
        rows="4"
        :maxlength="maxlength"
        :aria-invalid="!!errorMessage"
        :aria-describedby="errorId"
      ></textarea>
      <p v-if="errorMessage" :id="errorId" class="error" role="alert">{{ errorMessage }}</p>
      <button type="submit" :disabled="submitting">
        {{ submitting ? '送出中…' : `確定${label}` }}
      </button>
    </form>
  </div>
</template>

<style scoped>
.action {
  display: grid;
  gap: 0.5rem;
  justify-items: start;
}
form {
  display: grid;
  gap: 0.5rem;
  width: 100%;
  max-width: 640px;
}
textarea {
  font: inherit;
  padding: 0.4rem;
}
form button {
  justify-self: start;
}
.error {
  color: #c00;
  margin: 0;
}
</style>
