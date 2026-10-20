import { fileURLToPath, URL } from 'node:url'

import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'
import vueDevTools from 'vite-plugin-vue-devtools'

// 後端 API 的網址：CaseTrack.Api 的 launchSettings.json「https」設定檔（VS 預設用這個）。
// 不能指向 http 的 5286：https 設定檔也會聽 5286，但 UseHttpsRedirection 會回 307 轉到 https，
// 瀏覽器跟著轉過去就變成跨來源。用 http 設定檔啟動時，改用環境變數：CASETRACK_API_URL=http://localhost:5286
const apiUrl = process.env.CASETRACK_API_URL ?? 'https://localhost:7254'

// https://vite.dev/config/
export default defineConfig({
  plugins: [vue(), vueDevTools()],
  resolve: {
    alias: {
      '@': fileURLToPath(new URL('./src', import.meta.url)),
    },
  },
  server: {
    port: 5173,
    // port 被占用時直接失敗，不要自動換一個：換了之後網址跟預期的不一樣，很難發現
    strictPort: true,
    // 開發時由 Vite 把 API 請求轉給後端：瀏覽器只看到同一個來源（localhost:5173），不需要 CORS。
    // 正式環境也是前後端放在同一個網域（IIS 或 Nginx 轉發），開發和正式的架構一致
    // secure: false：後端用的是 ASP.NET Core 的開發憑證，Node.js 不信任它，不關掉驗證會連線失敗
    proxy: {
      '/api': { target: apiUrl, secure: false },
      '/dev': { target: apiUrl, secure: false },
    },
  },
})
