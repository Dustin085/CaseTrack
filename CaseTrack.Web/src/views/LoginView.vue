<script setup lang="ts">
import type { DevTokenRequest } from '@/api/types'
import { useAuth } from '@/composables/useAuth'
import { ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'

const errorMessage = ref<string | null>(null)
// 開發用的登入頁：選擇測試帳號，向 /dev/token 取得 token。正式環境換成 IdP（AD、單一登入）的登入畫面
const { login, homePath } = useAuth()
const router = useRouter()
const route = useRoute()

const requests: DevTokenRequest[] = [
  {
    userId: '7f3a2c91-8d64-4b0e-a521-6c9f2e71d843',
    name: '民眾A',
    roles: ['Citizen'],
    departmentId: null,
  },
  {
    userId: 'b2e8d415-6a73-49cf-8d20-f1a6c3e95704',
    name: '民眾B',
    roles: ['Citizen'],
    departmentId: null,
  },
  {
    userId: 'c91f6a28-3e45-47b2-a8d1-5f0c9e624713',
    name: '工務局承辦人',
    roles: ['CaseOfficer'],
    departmentId: 'f3c0543e-db7c-464b-8016-d71088ef6b83',
  },
  {
    userId: 'e5a4d2c8-71b3-4f96-9c0e-3d8b2a6f1e47',
    name: '工務局主管',
    roles: ['Supervisor'],
    departmentId: 'f3c0543e-db7c-464b-8016-d71088ef6b83',
  },
  {
    userId: '4d7b1e93-a526-4c80-9f34-2b8e6d1a705c',
    name: '分派人員',
    roles: ['Dispatcher'],
    departmentId: null,
  },
]

// 登入後要回到哪一頁。query 的值可能是 string、null 或陣列（?redirect=a&redirect=b），只接受一個字串；
// 而且只接受站內的路徑：開頭是 "/"，但不是 "//"（"//evil.com" 是省略協定的外部網址）。
// 網址是使用者可以任意修改的輸入，不能直接拿來導頁
function redirectTarget(): string {
  const redirect = route.query.redirect
  if (typeof redirect === 'string' && redirect.startsWith('/') && !redirect.startsWith('//')) {
    return redirect
  }
  // 沒有指定要去哪：依角色決定首頁（登入完成後 me 已經有值）
  return homePath.value
}

// 要 async/await：login() 回傳 Promise，不等它的話，
// ① 還沒登入完成就導頁，beforeEach 看到沒登入又導回登入頁；② 登入失敗的例外不會進到 catch
const handleLogin = async (request: DevTokenRequest) => {
  errorMessage.value = null
  try {
    await login(request)
    await router.push(redirectTarget())
  } catch (e) {
    errorMessage.value = e instanceof Error ? e.message : '發生未知錯誤'
  }
}
</script>

<template>
  <section class="card login">
    <h1>登入</h1>
    <!-- 開發用：選一個測試帳號，向 /dev/token 取得 token。正式環境換成 IdP 的登入畫面 -->
    <p class="muted">開發環境：選擇測試帳號登入</p>
    <div class="accounts">
      <button
        v-for="request in requests"
        :key="request.userId"
        type="button"
        @click="handleLogin(request)"
      >
        {{ request.name }}
      </button>
    </div>
    <p v-if="errorMessage" class="message error" role="alert">{{ errorMessage }}</p>
  </section>
</template>

<style scoped>
.login {
  max-width: 480px;
  margin: 2rem auto 0;
}
.accounts {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(140px, 1fr));
  gap: 0.75rem;
  margin-top: 1rem;
}
</style>
