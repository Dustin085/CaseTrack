<script setup lang="ts">
import {
  activateDepartment,
  createDepartment,
  deactivateDepartment,
  listAdminDepartments,
  MAX_DEPARTMENT_CODE_LENGTH,
  MAX_DEPARTMENT_NAME_LENGTH,
  renameDepartment,
} from '@/api/admin'
import { ApiError } from '@/api/client'
import type { Department } from '@/api/types'
import { onMounted, ref } from 'vue'

// 單位管理（管理者）：新增、改名、停用、啟用。沒有刪除：舊案件還指向這個單位。
// 每次異動後重新載入清單，畫面就是資料庫的現況（單位很少，不用自己在前端更新陣列）

const departments = ref<Department[] | null>(null)
const errorMessage = ref<string | null>(null)
const successMessage = ref<string | null>(null)
const busy = ref(false)

const newCode = ref('')
const newName = ref('')

// 同一時間只改一個單位的名稱
const editingId = ref<string | null>(null)
const editingName = ref('')

async function load() {
  try {
    departments.value = await listAdminDepartments()
  } catch (e) {
    errorMessage.value = messageOf(e, '載入單位時發生錯誤')
  }
}

onMounted(load)

// 400 的訊息在 errors 裡（例如「代碼 PWD 已經存在」），比 title 具體
function messageOf(e: unknown, fallback: string): string {
  const fieldError =
    e instanceof ApiError ? Object.values(e.problem?.errors ?? {}).flat()[0] : undefined
  return fieldError ?? (e instanceof Error ? e.message : fallback)
}

// 所有異動共用：防止重複送出、顯示結果、重新載入
async function perform(action: () => Promise<unknown>, success: string): Promise<boolean> {
  if (busy.value) {
    return false
  }
  busy.value = true
  errorMessage.value = null
  successMessage.value = null
  try {
    await action()
    successMessage.value = success
    await load()
    return true
  } catch (e) {
    errorMessage.value = messageOf(e, '儲存時發生錯誤')
    return false
  } finally {
    busy.value = false
  }
}

async function handleCreate() {
  const code = newCode.value.trim()
  const created = await perform(
    () => createDepartment(code, newName.value),
    `已新增 ${code.toUpperCase()}`,
  )
  if (created) {
    newCode.value = ''
    newName.value = ''
  }
}

function startEdit(department: Department) {
  editingId.value = department.id
  editingName.value = department.name
}

async function handleRename(department: Department) {
  if (await perform(() => renameDepartment(department.id, editingName.value), '已更新名稱')) {
    editingId.value = null
  }
}

// 停用要確認：之後就不能再分派案件給這個單位
async function toggleActive(department: Department) {
  if (department.isActive) {
    if (
      !window.confirm(
        `停用「${department.name}」後就不能再分派案件給這個單位，已分派的案件不受影響。確定停用？`,
      )
    ) {
      return
    }
    await perform(() => deactivateDepartment(department.id), `已停用 ${department.name}`)
  } else {
    await perform(() => activateDepartment(department.id), `已啟用 ${department.name}`)
  }
}
</script>

<template>
  <h1>單位管理</h1>

  <p v-if="errorMessage" class="message error" role="alert">{{ errorMessage }}</p>
  <p v-if="successMessage" class="message success" role="status">{{ successMessage }}</p>

  <section class="card">
    <h2>新增單位</h2>
    <form class="row" @submit.prevent="handleCreate">
      <label for="department-code">代碼</label>
      <input
        id="department-code"
        v-model="newCode"
        required
        pattern="[A-Za-z0-9]+"
        :maxlength="MAX_DEPARTMENT_CODE_LENGTH"
        aria-describedby="department-code-hint"
        class="code"
      />
      <label for="department-name">名稱</label>
      <input
        id="department-name"
        v-model="newName"
        required
        :maxlength="MAX_DEPARTMENT_NAME_LENGTH"
      />
      <button type="submit" :disabled="busy">新增</button>
    </form>
    <p id="department-code-hint" class="muted">
      代碼是英文字母或數字（會轉成大寫），建立後不能修改。
    </p>
  </section>

  <p v-if="departments === null && !errorMessage">載入中…</p>
  <table v-else-if="departments">
    <thead>
      <tr>
        <th scope="col">代碼</th>
        <th scope="col">名稱</th>
        <th scope="col">狀態</th>
        <th scope="col"><span class="visually-hidden">操作</span></th>
      </tr>
    </thead>
    <tbody>
      <tr v-for="d in departments" :key="d.id" :class="{ inactive: !d.isActive }">
        <td>{{ d.code }}</td>
        <td>
          <form v-if="editingId === d.id" class="row" @submit.prevent="handleRename(d)">
            <input
              v-model="editingName"
              required
              :maxlength="MAX_DEPARTMENT_NAME_LENGTH"
              :aria-label="`${d.code} 的新名稱`"
            />
            <button type="submit" :disabled="busy">儲存</button>
            <button type="button" @click="editingId = null">取消</button>
          </form>
          <template v-else>{{ d.name }}</template>
        </td>
        <td>{{ d.isActive ? '啟用' : '已停用' }}</td>
        <td class="actions">
          <button
            v-if="editingId !== d.id"
            type="button"
            :disabled="busy"
            :aria-label="`修改 ${d.name} 的名稱`"
            @click="startEdit(d)"
          >
            改名
          </button>
          <!-- 每一列都有「停用」，螢幕報讀軟體要念出是哪個單位 -->
          <button
            type="button"
            :disabled="busy"
            :aria-label="`${d.isActive ? '停用' : '啟用'} ${d.name}`"
            @click="toggleActive(d)"
          >
            {{ d.isActive ? '停用' : '啟用' }}
          </button>
        </td>
      </tr>
    </tbody>
  </table>
</template>

<style scoped>
.row {
  display: flex;
  flex-wrap: wrap;
  gap: 0.5rem;
  align-items: center;
}
.code {
  width: 8rem;
}
.actions {
  display: flex;
  gap: 0.5rem;
  justify-content: flex-end;
}
tr.inactive td {
  color: var(--color-muted);
}
</style>
