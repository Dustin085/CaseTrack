<script setup lang="ts"></script>

<template>
  <header>
    <strong>CaseTrack 陳情案件系統</strong>
    <nav>
      <RouterLink to="/my/cases">我的案件</RouterLink>
      <RouterLink to="/my/cases/new">我要陳情</RouterLink>
      <RouterLink to="/login">登入</RouterLink>
    </nav>
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
main {
  padding: 1rem;
  max-width: 960px;
}
</style>
