// 後端回傳的時間是 UTC 的 ISO 字串，顯示成台灣時間，例如 "2026/10/21 10:30"。
// - 一定要指定 timeZone：不指定就用使用者電腦的時區，而且 UTC 的晚上在台灣已經是隔天
// - 每個欄位都要列出來：只寫 hour12、timeZone 的話，預設只顯示日期，不會有時間
// - 建立一次重複使用：建立格式化物件的成本不低，列表每一筆都建立一次很浪費
const dateTimeFormatter = new Intl.DateTimeFormat('zh-TW', {
  year: 'numeric',
  month: '2-digit',
  day: '2-digit',
  hour: '2-digit',
  minute: '2-digit',
  hour12: false,
  timeZone: 'Asia/Taipei',
})

export function formatDateTime(iso: string): string {
  const date = new Date(iso)
  // 不合法的字串不會在 new Date 時失敗，而是得到 Invalid Date（getTime() 是 NaN），
  // 拿去 format() 才會拋 RangeError，整個頁面就壞了。
  // 回傳原本的字串：使用者至少看得到原始值，除錯時也找得到是哪個值有問題
  if (Number.isNaN(date.getTime())) {
    return iso
  }
  return dateTimeFormatter.format(date)
}
