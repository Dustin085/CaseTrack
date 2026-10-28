<script setup lang="ts">
import { ApiError } from '@/api/client'
import { submitCase } from '@/api/myCases'
import type { SubmitCaseRequest } from '@/api/types'
import { reactive, ref } from 'vue'
import { useRouter } from 'vue-router'

// 民眾送出陳情。驗證以後端為準：前端只用 maxlength 限制長度，
// 必填、「電話與 Email 至少一項」都由後端檢查，錯誤再對應回欄位（novalidate 關掉瀏覽器內建的驗證泡泡）
const router = useRouter()

// reactive：有很多欄位的物件，讀寫時不用寫 .value（form.subject）
const form = reactive({
  subject: '',
  content: '',
  petitioner: { name: '', phone: '', email: '', address: '' },
})

// 後端的驗證錯誤：key 是 C# 的屬性路徑（"Subject"、"Petitioner.Name"），直接拿來對應欄位
const fieldErrors = ref<Record<string, string[]>>({})
// 表單上方的錯誤：Domain 的規則（沒有 errors，只有 detail）或其他錯誤
const formError = ref<string | null>(null)
const submitting = ref(false)

function errorFor(key: string): string | undefined {
  return fieldErrors.value[key]?.[0]
}

// 選填欄位：空白送 null（「沒有填」），不是空字串
function optional(value: string): string | null {
  return value.trim() || null
}

async function handleSubmit() {
  // 真正擋住重複送出的是這一行：按鈕的 disabled 要等 Vue 下一輪更新畫面才生效，
  // 在那之前連點、或在輸入框按 Enter，都還會再觸發一次
  if (submitting.value) {
    return
  }
  fieldErrors.value = {}
  formError.value = null
  submitting.value = true

  const request: SubmitCaseRequest = {
    subject: form.subject.trim(),
    content: form.content.trim(),
    petitioner: {
      name: form.petitioner.name.trim(),
      phone: optional(form.petitioner.phone),
      email: optional(form.petitioner.email),
      address: optional(form.petitioner.address),
    },
  }

  try {
    // 附件的選擇元件還沒接上，先送空的清單
    const result = await submitCase(request, [])
    await router.push({ name: 'my-case-detail', params: { id: result.id } })
  } catch (e) {
    if (e instanceof ApiError && e.problem?.errors) {
      // 驗證失敗的 title 是框架的英文預設值，不顯示給民眾
      fieldErrors.value = e.problem.errors
      formError.value = '請修正標示的欄位'
    } else {
      // Domain 的錯誤（例如「電話與 Email 至少需填寫一項」）：ApiError 的 message 就是 detail
      formError.value = e instanceof Error ? e.message : '送出時發生錯誤，請稍後再試'
    }
  } finally {
    submitting.value = false
  }
}
</script>

<template>
  <h1>我要陳情</h1>

  <!-- 只用表單的 @submit，按鈕不要再綁 click：type="submit" 的按鈕被點擊時，
       click 和表單的 submit 都會觸發，handleSubmit 會執行兩次，送出兩件案件 -->
  <form novalidate @submit.prevent="handleSubmit">
    <p v-if="formError" class="form-error" role="alert">{{ formError }}</p>

    <fieldset>
      <legend>陳情內容</legend>

      <div class="field">
        <label for="subject">主旨（必填）</label>
        <input
          id="subject"
          v-model="form.subject"
          maxlength="200"
          :aria-invalid="!!errorFor('Subject')"
          aria-describedby="subject-error"
        />
        <p v-if="errorFor('Subject')" id="subject-error" class="error">{{ errorFor('Subject') }}</p>
      </div>

      <div class="field">
        <label for="content">內容（必填）</label>
        <textarea
          id="content"
          v-model="form.content"
          rows="8"
          :aria-invalid="!!errorFor('Content')"
          aria-describedby="content-error"
        ></textarea>
        <p v-if="errorFor('Content')" id="content-error" class="error">{{ errorFor('Content') }}</p>
      </div>
    </fieldset>

    <fieldset>
      <legend>陳情人資料</legend>
      <p class="hint">電話與 Email 至少填寫一項，承辦人需要補件或回覆時才聯絡得到您。</p>

      <div class="field">
        <label for="petitioner-name">姓名（必填）</label>
        <input
          id="petitioner-name"
          v-model="form.petitioner.name"
          maxlength="50"
          autocomplete="name"
          :aria-invalid="!!errorFor('Petitioner.Name')"
          aria-describedby="petitioner-name-error"
        />
        <p v-if="errorFor('Petitioner.Name')" id="petitioner-name-error" class="error">
          {{ errorFor('Petitioner.Name') }}
        </p>
      </div>

      <div class="field">
        <label for="petitioner-phone">電話</label>
        <input
          id="petitioner-phone"
          v-model="form.petitioner.phone"
          type="tel"
          maxlength="20"
          autocomplete="tel"
          :aria-invalid="!!errorFor('Petitioner.Phone')"
          aria-describedby="petitioner-phone-error"
        />
        <p v-if="errorFor('Petitioner.Phone')" id="petitioner-phone-error" class="error">
          {{ errorFor('Petitioner.Phone') }}
        </p>
      </div>

      <div class="field">
        <label for="petitioner-email">Email</label>
        <input
          id="petitioner-email"
          v-model="form.petitioner.email"
          type="email"
          maxlength="254"
          autocomplete="email"
          :aria-invalid="!!errorFor('Petitioner.Email')"
          aria-describedby="petitioner-email-error"
        />
        <p v-if="errorFor('Petitioner.Email')" id="petitioner-email-error" class="error">
          {{ errorFor('Petitioner.Email') }}
        </p>
      </div>

      <div class="field">
        <label for="petitioner-address">地址</label>
        <input
          id="petitioner-address"
          v-model="form.petitioner.address"
          maxlength="200"
          autocomplete="street-address"
          :aria-invalid="!!errorFor('Petitioner.Address')"
          aria-describedby="petitioner-address-error"
        />
        <p v-if="errorFor('Petitioner.Address')" id="petitioner-address-error" class="error">
          {{ errorFor('Petitioner.Address') }}
        </p>
      </div>
    </fieldset>

    <button type="submit" :disabled="submitting">{{ submitting ? '送出中…' : '送出' }}</button>
  </form>
</template>

<style scoped>
form {
  display: grid;
  gap: 1rem;
  max-width: 640px;
}
fieldset {
  display: grid;
  gap: 0.75rem;
  border: 1px solid #ddd;
  padding: 1rem;
}
.field {
  display: grid;
  gap: 0.25rem;
}
input,
textarea {
  font: inherit;
  padding: 0.4rem;
}
[aria-invalid='true'] {
  border-color: #c00;
}
.error,
.form-error {
  color: #c00;
  margin: 0;
}
.hint {
  margin: 0;
  color: #555;
}
button {
  justify-self: start;
  padding: 0.5rem 1.5rem;
}
</style>
