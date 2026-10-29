<script setup lang="ts">
import { watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useAuth } from '@/composables/useAuth'

const { me, isLoggedIn, isCitizen, isStaff, isAdmin, logout } = useAuth()
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
    <!-- 依角色顯示選單（只是使用者體驗，權限在後端）。重新整理後 me 還沒回來前不顯示 -->
    <nav v-if="isLoggedIn">
      <RouterLink v-if="isStaff" to="/cases">案件列表</RouterLink>
      <RouterLink v-if="isStaff" to="/reports">報表</RouterLink>
      <template v-if="isCitizen">
        <RouterLink to="/my/cases">我的案件</RouterLink>
        <RouterLink to="/my/cases/new">我要陳情</RouterLink>
      </template>
      <template v-if="isAdmin">
        <RouterLink to="/admin/departments">單位管理</RouterLink>
        <RouterLink to="/admin/audit-entries">稽核紀錄</RouterLink>
      </template>
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
  gap: 2rem;
  align-items: center;
  padding: 0.75rem 1.5rem;
  background: var(--color-surface);
  border-bottom: 1px solid var(--color-border);
  box-shadow: var(--shadow);
}
header strong {
  font-size: 1.1rem;
  color: var(--color-primary);
}
nav {
  display: flex;
  gap: 1.25rem;
}
nav a {
  color: var(--color-text);
  text-decoration: none;
  padding: 0.25rem 0;
  border-bottom: 2px solid transparent;
}
/* 目前所在的頁面（Vue Router 會自動加上這個 class） */
nav a.router-link-active {
  color: var(--color-primary);
  border-bottom-color: var(--color-primary);
}
.user {
  display: flex;
  gap: 0.75rem;
  align-items: center;
  margin-left: auto;
  color: var(--color-muted);
}
main {
  padding: 1.5rem;
  max-width: 960px;
  margin: 0 auto;
}
</style>
