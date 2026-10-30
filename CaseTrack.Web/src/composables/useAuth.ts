import { apiFetch } from '@/api/client'
import { type DevTokenRequest, type DevTokenResponse, type Me, type Role } from '@/api/types'
import { computed, readonly, ref } from 'vue'

// token 存在 sessionStorage：重新整理後還在（開發時不用一直重新登入），關掉分頁就清掉；
// 每個分頁各自獨立，可以一個分頁登入民眾、另一個分頁登入承辦人。
// 注意：XSS 一樣讀得到 sessionStorage。正式環境應該用 BFF，token 留在後端，瀏覽器只拿 HttpOnly cookie
const TOKEN_STORAGE_KEY = 'casetrack.accessToken'

// 寫在 useAuth() 外面：整個 app 只有一份狀態，每個元件呼叫 useAuth() 拿到的都是同一份。
// 寫在裡面的話，每個元件各有一份，一個元件登入了，其他元件還是「沒登入」。
// token 是程式用的那一份（響應式，畫面會跟著更新）；sessionStorage 只是存檔，載入時讀一次、改變時寫一次
const token = ref<string | null>(sessionStorage.getItem(TOKEN_STORAGE_KEY))

// 後端認定的身分（GET /api/me）。重新整理後是 null，要由 loadMe() 重新取得
const me = ref<Me | null>(null)

// 正在進行中的 /api/me 請求：同時有多個地方呼叫 loadMe()（例如換頁時），只送一次
let loadingMe: Promise<void> | null = null

function setToken(value: string | null) {
  token.value = value
  if (value === null) {
    sessionStorage.removeItem(TOKEN_STORAGE_KEY)
  } else {
    sessionStorage.setItem(TOKEN_STORAGE_KEY, value)
  }
}

// 最近一次登出的原因，App.vue 導向登入頁時用來決定要不要帶 redirect：
// 'user' 是按下登出按鈕（使用者已經決定離開，不帶；共用電腦上下一個人也不會被帶到上一個人的頁面），
// 'expired' 是操作到一半 token 過期（API 回 401），重新登入後應該回到原本的頁面
export type LogoutReason = 'user' | 'expired'
const logoutReason = ref<LogoutReason | null>(null)

// 先記原因再清 token：watch(isLoggedIn) 執行時原因已經是這次的
function logout(reason: LogoutReason = 'user') {
  logoutReason.value = reason
  setToken(null)
  me.value = null
}

// 有 token 卻還沒有 me（重新整理之後）：向後端取得身分。
// token 過期的話 /api/me 會回 401，apiFetch 會登出並導回登入頁
function loadMe(): Promise<void> {
  if (token.value === null || me.value !== null) {
    return Promise.resolve()
  }
  loadingMe ??= apiFetch<Me>('/api/me')
    .then((result) => {
      me.value = result
    })
    .finally(() => {
      loadingMe = null
    })
  return loadingMe
}

async function login(request: DevTokenRequest) {
  try {
    const { accessToken } = await apiFetch<DevTokenResponse>('/dev/token', {
      method: 'POST',
      body: request,
    })
    setToken(accessToken)
    me.value = await apiFetch<Me>('/api/me')
  } catch (e) {
    // 不要留下「有 token、沒有 me」的半套狀態；例外往外拋，讓登入頁顯示錯誤
    logout()
    throw e
  }
}

// isLoggedIn 看 token 不看 me：token 載入時就有（同步），me 要等 /api/me 回來（非同步），
// 用 me 判斷的話，重新整理後在 /api/me 回應之前都會被當成沒登入
const isLoggedIn = computed(() => token.value !== null)

// 依角色決定畫面（選單、登入後的首頁）。只是使用者體驗：真正的權限在後端，
// 民眾打開 /cases、承辦人打開 /my/cases，後端都會回 403
// satisfies：檢查每個值都是 Role（打錯字會編譯失敗），但型別維持字串陣列，才能拿 me.roles 的 string 來比對
const STAFF_ROLES: readonly string[] = ['CaseOfficer', 'Supervisor', 'Dispatcher'] satisfies Role[]
const CITIZEN: Role = 'Citizen'
const ADMINISTRATOR: Role = 'Administrator'

const isCitizen = computed(() => me.value?.roles.includes(CITIZEN) ?? false)
const isStaff = computed(() => me.value?.roles.some((r) => STAFF_ROLES.includes(r)) ?? false)
const isAdmin = computed(() => me.value?.roles.includes(ADMINISTRATOR) ?? false)
// 登入後的首頁：承辦端優先（承辦人自己也陳情時，平常主要的工作在承辦端）。
// 管理者沒有案件的權限，不能落到 /my/cases（民眾的頁面，後端會回 403）
const homePath = computed(() => {
  if (isStaff.value) {
    return '/cases'
  }
  return isAdmin.value ? '/admin/departments' : '/my/cases'
})

export function useAuth() {
  // readonly：狀態只能透過 login、logout 修改，元件改 me.value 時 TypeScript 會報錯
  return {
    me: readonly(me),
    isLoggedIn,
    isCitizen,
    isStaff,
    isAdmin,
    homePath,
    logoutReason: readonly(logoutReason),
    login,
    logout,
    loadMe,
  }
}

// 給 apiFetch 用：每次呼叫 API 時讀取目前的 token。
// 注意 client.ts 和這個檔案互相 import（循環）：兩邊都只在函式被呼叫時才用到對方，所以是安全的；
// 不要在檔案最外層直接呼叫對方的函式，那時對方可能還沒載入完成
export function getAccessToken(): string | null {
  return token.value
}
