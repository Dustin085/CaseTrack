<script setup lang="ts">
import { watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useAuth } from '@/composables/useAuth'

const { me, isLoggedIn, logout } = useAuth()
const router = useRouter()
const route = useRoute()

// 登出時導向登入頁，所有登出的情況都由這裡處理：
// 按下登出按鈕、在頁面上操作時 token 過期（apiFetch 收到 401 會呼叫 logout()）。
// 換頁時的檢查在 router 的 beforeEach；這裡處理的是「停在同一頁、狀態變了」的情況
watch(isLoggedIn, (loggedIn) => {
  if (!loggedIn && route.name !== 'login') {
    router.push({ name: 'login', query: { redirect: route.fullPath } })
  }
})
</script>

<template>
  <header>
    <strong>CaseTrack 陳情案件系統</strong>
    <nav v-if="isLoggedIn">
      <RouterLink to="/my/cases">我的案件</RouterLink>
      <RouterLink to="/my/cases/new">我要陳情</RouterLink>
    </nav>
    <div class="user">
      <template v-if="isLoggedIn">
        <!-- 重新整理後 me 要等 /api/me 回來才有值，這段期間不顯示名字 -->
        <span v-if="me">{{ me.name ?? me.userId }}</span>
        <button type="button" @click="logout">登出</button>
      </template>
      <RouterLink v-else to="/login">登入</RouterLink>
    </div>
  </header>
  <main>
    <RouterView />
  </main>
</template>

<style scoped>
header {
  display: flex;
  gap: 1.5rem;
  align-items: center;
  padding: 0.75rem 1rem;
  border-bottom: 1px solid #ddd;
}
nav {
  display: flex;
  gap: 1rem;
}
.user {
  display: flex;
  gap: 0.75rem;
  align-items: center;
  margin-left: auto;
}
main {
  padding: 1rem;
  max-width: 960px;
}
</style>
