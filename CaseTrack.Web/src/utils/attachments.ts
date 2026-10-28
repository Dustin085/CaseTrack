// 附件的規則，複製自後端 Domain 的 AttachmentRules：後端改了這裡要跟著改。
// 前端的檢查只是為了體驗（選了馬上知道，不用等上傳完），以及超過整個請求的上限時，
// 瀏覽器常常讀不到 413（連線被重設，fetch 只丟出 TypeError）。安全的檢查在後端（檔頭判斷實際類型），
// 這裡只看副檔名：檔名可以造假，所以不能取代後端
export const MAX_ATTACHMENT_COUNT = 5
export const MAX_ATTACHMENT_SIZE = 10 * 1024 * 1024
export const MAX_FILE_NAME_LENGTH = 255

// 允許的副檔名只寫在這裡；<input accept> 用的字串由它組出來
const ALLOWED_EXTENSIONS = ['.jpg', '.jpeg', '.png', '.pdf']
export const ATTACHMENT_ACCEPT = ALLOWED_EXTENSIONS.join(',')

// 顯示在選擇檔案的地方，讓使用者選之前就知道規則
export const ATTACHMENT_HINT = `最多 ${MAX_ATTACHMENT_COUNT} 個，每個 ${MAX_ATTACHMENT_SIZE / 1024 / 1024} MB 以內，可上傳 JPG、PNG、PDF`

// 檢查「整份清單」（已經選的加上這次選的），回傳第一個錯誤訊息，沒有錯誤就回 null。
// 訊息和後端的 AttachmentRules 一樣，使用者不管看到哪一邊的錯誤都一致
export function validateAttachments(files: File[]): string | null {
  if (files.length > MAX_ATTACHMENT_COUNT) {
    return `附件最多 ${MAX_ATTACHMENT_COUNT} 個`
  }
  for (const file of files) {
    if (file.name.length > MAX_FILE_NAME_LENGTH) {
      return `附件的檔名不能超過 ${MAX_FILE_NAME_LENGTH} 個字`
    }
    if (!ALLOWED_EXTENSIONS.includes(getExtension(file.name))) {
      return `不支援的檔案類型：${file.name}（只接受 JPG、PNG、PDF）`
    }
    // 例如雲端硬碟裡還沒下載到電腦的檔案
    if (file.size <= 0) {
      return `附件是空的：${file.name}`
    }
    if (file.size > MAX_ATTACHMENT_SIZE) {
      return `附件不能超過 ${MAX_ATTACHMENT_SIZE / 1024 / 1024} MB：${file.name}`
    }
  }
  return null
}

// 小寫的副檔名（含 "."）；沒有副檔名回傳空字串
function getExtension(fileName: string): string {
  const index = fileName.lastIndexOf('.')
  return index === -1 ? '' : fileName.slice(index).toLowerCase()
}
