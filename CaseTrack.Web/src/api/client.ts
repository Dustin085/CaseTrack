import type { ProblemDetails } from '@/api/types'
import { getAccessToken, useAuth } from '@/composables/useAuth'

// API 回傳錯誤（4xx、5xx）。保留狀態碼和 ProblemDetails，呼叫端依照狀態決定怎麼顯示：
// 400 把 errors 顯示在欄位旁邊、404 顯示「找不到案件」、409 顯示 detail
export class ApiError extends Error {
  readonly status: number
  readonly problem: ProblemDetails | null

  constructor(status: number, problem: ProblemDetails | null) {
    super(messageFor(status, problem))
    this.name = 'ApiError'
    this.status = status
    this.problem = problem
  }
}

// message 是頁面直接顯示給使用者的文字，一定要是中文：
// - 502、503、504：後端停了、重新部署中或逾時。回應來自前面的 proxy（開發時是 Vite，正式環境是 IIS／Nginx），
//   fetch 有收到回應，所以不是 NetworkError
// - 其他 5xx：後端的 ProblemDetails 是框架的英文預設值（"An error occurred while processing your request."），
//   不能顯示；保留狀態碼，民眾回報時客服才知道是哪一種錯誤
// - 403：授權失敗沒有 ProblemDetails，不能顯示「請稍後再試」（再試幾次都一樣）
// - 4xx：用後端的說明（Domain 的錯誤訊息是中文的 detail）
function messageFor(status: number, problem: ProblemDetails | null): string {
  if (status === 502 || status === 503 || status === 504) {
    return '系統暫時無法使用，請稍後再試'
  }
  if (status === 403 && problem === null) {
    return '您沒有權限使用這個功能'
  }
  if (status >= 500) {
    return `系統發生錯誤（HTTP ${status}），請稍後再試`
  }
  return problem?.detail ?? problem?.title ?? `發生錯誤（HTTP ${status}），請稍後再試`
}

// 沒有收到任何回應（斷線、後端沒啟動、上傳太大被中斷連線）。跟 ApiError 分開：
// ApiError 是「後端說不行」，這個是「根本沒連上」。訊息是中文，頁面直接顯示 message 就好，
// 不會把 fetch 原本的英文 "Failed to fetch" 給民眾看。原本的錯誤放在 cause，除錯時在 console 看得到。
// 預設訊息提到檔案大小，是寫給上傳的（太大時連線被中斷，讀不到 413）；下載等其他情況要傳入自己的 message
export class NetworkError extends Error {
  constructor(options?: ErrorOptions & { message?: string }) {
    super(
      options?.message ?? '無法連線到伺服器，請確認網路後再試；如果有附件，請確認檔案大小',
      options,
    )
    this.name = 'NetworkError'
  }
}

// 呼叫後端 API，成功時回傳解析後的 JSON。錯誤的處理見 send。
// T 是呼叫端的承諾，不是檢查：TypeScript 的型別在執行時都被擦掉了，response.json() 拿到什麼就是什麼
export async function apiFetch<T>(
  path: string,
  options?: { method?: string; body?: unknown },
): Promise<T> {
  const response = await send(path, options)

  // 204 No Content：成功但沒有內容（補件、受理、結案），這時呼叫 response.json() 會拋例外。
  // 呼叫端要寫 apiFetch<void>(...)，as T 才是對的
  if (response.status === 204) {
    return undefined as T
  }

  return (await response.json()) as T
}

// 下載檔案（附件）。不能用 <a href="/api/...">：瀏覽器換頁不會帶 Authorization 標頭，後端會回 401。
// 所以用 fetch 帶 token 把檔案讀成 Blob，再產生 blob: 網址交給 <a download> 存檔。
// - 一律用 download 存成檔案，不要用 window.open 打開：blob: 網址繼承這個網站的 origin，
//   萬一有偽裝的 HTML 通過檢查，打開時裡面的 script 會在我們的網站上執行（可以讀到 token）
// - 整個檔案會先讀進記憶體，下載完才存檔（瀏覽器的下載列不會顯示進度）。只適合附件這種小檔案（上限 10MB），
//   之後的大檔案（例如報表匯出）要改成後端發的短期下載網址，用 <a href> 直接下載
// - 檔名由呼叫端傳入（詳情裡已經有），不解析 Content-Disposition：filename*=UTF-8''... 要自己解碼，
//   而且前端與 API 不同網域時，後端沒加 Access-Control-Expose-Headers 就讀不到這個標頭（只會拿到 null）
export async function apiDownload(path: string, fileName: string): Promise<void> {
  const networkErrorMessage = '無法下載檔案，請確認網路後再試'
  const response = await send(path, undefined, networkErrorMessage)

  // send 的 try/catch 只涵蓋到收到標頭為止，body 是在這裡才讀的：傳到一半斷線也是連線的問題
  let blob: Blob
  try {
    blob = await response.blob()
  } catch (e) {
    throw new NetworkError({ cause: e, message: networkErrorMessage })
  }

  const url = URL.createObjectURL(blob)

  // <a> 不用加到頁面上，click() 就會下載
  const link = document.createElement('a')
  link.href = url
  link.download = fileName
  link.click()

  // 只要 blob: 網址還在，Blob 就不會被回收，所以一定要 revoke（否則每下載一次就多佔一份記憶體，直到關掉分頁）。
  // 等下一輪再 revoke：click() 之後馬上 revoke，有些瀏覽器還沒開始下載就找不到資料
  setTimeout(() => URL.revokeObjectURL(url), 0)
}

// apiFetch 和 apiDownload 共用：送出請求，回傳成功的 Response，兩者只差在怎麼讀取內容。
// 有回應但失敗時拋出 ApiError（呼叫端用 e.status 判斷），沒有收到回應時拋出 NetworkError。
// networkErrorMessage 是 NetworkError 的訊息，沒傳的話用預設的（寫給上傳的）
async function send(
  path: string,
  options?: { method?: string; body?: unknown },
  networkErrorMessage?: string,
): Promise<Response> {
  const headers: Record<string, string> = {}
  const token = getAccessToken()
  if (token) {
    headers['Authorization'] = `Bearer ${token}`
  }

  const init: RequestInit = { method: options?.method ?? 'GET', headers }
  if (options?.body instanceof FormData) {
    // 不要設定 Content-Type：瀏覽器會自動加上 multipart/form-data; boundary=...（分隔字串是組 body 時才產生的），
    // 自己設定的話會少了 boundary，後端讀不懂 body
    init.body = options.body
  } else if (options?.body !== undefined) {
    // 用 !== undefined：body 是 0、''、false 時也是合法的內容，不能當成沒有 body
    // 不設定的話，fetch 送字串的預設是 text/plain，ASP.NET Core 會回 415
    headers['Content-Type'] = 'application/json'
    init.body = JSON.stringify(options.body)
  }

  let response: Response
  try {
    response = await fetch(path, init)
  } catch (e) {
    // fetch 沒有收到任何回應時才會丟出例外（4xx、5xx 不會，那些在下面處理）：
    // 斷線、後端沒啟動、上傳太大被中斷連線（瀏覽器讀不到 413）。
    // 之後如果加上 AbortSignal（例如換頁時取消請求），AbortError 要直接往外丟，不能包成 NetworkError
    throw new NetworkError({ cause: e, message: networkErrorMessage })
  }

  if (!response.ok) {
    // token 過期或無效：登出，然後拋出例外（呼叫端才不會當成成功繼續往下執行）。
    // 這裡不導頁：apiFetch 只負責呼叫 API。換頁時由 router 的 beforeEach 導向登入頁（它知道使用者要去哪），
    // 在頁面上操作時由 App.vue 監看 isLoggedIn 導頁。
    // 在 beforeEach 裡呼叫 router.push 會開始另一次換頁，原本的換頁被取消，redirect 會帶錯頁面
    if (response.status === 401) {
      // useAuth() 只是讀寫檔案層級的狀態，沒有用到 inject()，在元件外面呼叫也可以（跟 useRouter() 不同）
      useAuth().logout('expired')
      throw new ApiError(401, null)
    }

    // Controller 的錯誤是 "application/problem+json; charset=utf-8"，不能用 === 比對
    const contentType = response.headers.get('Content-Type') ?? ''
    if (contentType.startsWith('application/problem+json')) {
      throw new ApiError(response.status, (await response.json()) as ProblemDetails)
    }

    // 沒有 ProblemDetails：例如後端沒啟動時 Vite proxy 回的 502。保留狀態碼，呼叫端還能判斷
    throw new ApiError(response.status, null)
  }

  return response
}
