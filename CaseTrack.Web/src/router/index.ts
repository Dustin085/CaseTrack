import { createRouter, createWebHistory } from 'vue-router'
import LoginView from '@/views/LoginView.vue'
import { useAuth } from '@/composables/useAuth'

// 登入頁直接載入；其他頁面用 () => import(...) 延遲載入：打包時各自拆成獨立的檔案，進到那一頁才下載
const router = createRouter({
  history: createWebHistory(import.meta.env.BASE_URL),
  routes: [
    { path: '/', redirect: '/my/cases' },
    { path: '/login', name: 'login', component: LoginView },
    {
      path: '/my/cases',
      name: 'my-cases',
      component: () => import('@/views/my/MyCaseListView.vue'),
    },
    {
      path: '/my/cases/new',
      name: 'submit-case',
      component: () => import('@/views/my/SubmitCaseView.vue'),
    },
    {
      // props: true：把路由參數 id 當成元件的 prop 傳進去，元件不用自己讀 useRoute()
      path: '/my/cases/:id',
      name: 'my-case-detail',
      component: () => import('@/views/my/MyCaseDetailView.vue'),
      props: true,
    },
  ],
})

// 沒登入時導向登入頁，登入後回到原本要去的頁面。
// 這只是使用者體驗：真正擋住沒登入請求的是後端（401），前端的檢查可以被繞過
router.beforeEach(async (to) => {
  // 登入頁一定要放行，否則「導向登入頁 → 沒登入 → 導向登入頁」會無限循環。
  // 用路由名稱比對，不用路徑：之後改網址只要改路由設定
  if (to.name === 'login') {
    return
  }

  const { isLoggedIn, loadMe } = useAuth()
  const toLogin = { name: 'login', query: { redirect: to.fullPath } }

  // isLoggedIn 是 ComputedRef（物件），要讀 .value：!isLoggedIn 永遠是 false，沒登入也會放行
  if (!isLoggedIn.value) {
    return toLogin
  }

  // 重新整理後要重新取得身分。在顯示頁面之前做：token 過期的話，受保護的頁面不會先閃出來
  try {
    await loadMe()
  } catch {
    // 401：apiFetch 已經登出了；其他錯誤（例如後端沒啟動）：到登入頁重新登入時，會在畫面上顯示錯誤
    return toLogin
  }
})

export default router
