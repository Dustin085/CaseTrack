using CaseTrack.Domain.Cases;

namespace CaseTrack.Api.Common;

// 整個請求的上限：附件的上限加上文字欄位與 multipart 格式的空間（1 MB）。
// Domain 的單檔 10 MB 要等整個請求收完才會檢查；這裡是一邊收一邊數，超過就中斷，不會先把 10 GB 收進暫存檔再拒絕。
// Kestrel 預設只收 30,000,000 bytes（約 28.6 MB）。部署到 IIS 時，web.config 的 maxAllowedContentLength 也要調整，
// 否則 IIS 會先回 404.13，請求到不了 Kestrel
public static class UploadLimits
{
    public const long MaxRequestBodySize =
        AttachmentRules.MaxCountPerSubmission * AttachmentRules.MaxSize + 1024 * 1024;
}
