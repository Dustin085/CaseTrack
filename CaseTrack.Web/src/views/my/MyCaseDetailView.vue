<script setup lang="ts">
// 時段 4：GET /api/my/cases/{id}；狀態是 AwaitingSupplement 時顯示補件表單
// 路由參數 id 由 router 的 props: true 傳進來
defineProps<{ id: string }>()
</script>

<template>
  <h1>案件詳情</h1>
</template>
