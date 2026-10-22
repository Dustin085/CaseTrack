<script setup lang="ts">
// 案件狀態的標籤：不同狀態不同顏色，列表和詳情頁共用。
// 顏色只是輔助，文字本身就說明了狀態（色弱的使用者、黑白列印也看得懂）
import type { CaseStatus } from '@/api/types'
import { caseStatusLabels } from '@/utils/caseStatus'

defineProps<{ status: CaseStatus }>()
</script>

<template>
  <span class="badge" :class="status">{{ caseStatusLabels[status] }}</span>
</template>

<style scoped>
.badge {
  display: inline-block;
  padding: 0.1rem 0.6rem;
  border-radius: 999px;
  font-size: 0.85rem;
  font-weight: 600;
  white-space: nowrap;
}
.Submitted {
  background: #e8eefc;
  color: #1d4ed8;
}
.UnderReview {
  background: #fff4d6;
  color: #92400e;
}
.AwaitingSupplement {
  background: #fde8e8;
  color: #9b1c1c;
}
.Closed {
  background: #e6f4ea;
  color: #1b6e3c;
}
.Rejected {
  background: #eceef1;
  color: #4b5563;
}
</style>
