import { createRouter, createWebHistory } from 'vue-router'
import LoginView from '@/views/LoginView.vue'

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

// 時段 3：加上 router.beforeEach，沒登入時導向 /login

export default router
