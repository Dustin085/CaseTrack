using CaseTrack.Application.Cases;
using Microsoft.AspNetCore.Mvc;

namespace CaseTrack.Api.Common;

// 民眾端和承辦端的下載共用：安全相關的標頭只寫在這裡，新增下載端點不會漏掉
public static class AttachmentResults
{
    // nosniff：瀏覽器一律照 Content-Type 處理，不自己猜類型（不會把內容像網頁的檔案當成 HTML 執行）。
    // 給了檔名 → Content-Disposition: attachment：一律下載，不在頁面裡開啟（PDF 可以內嵌 JavaScript）；
    // 中文檔名會自動加上 filename*=UTF-8''...。Stream 由框架在傳送完之後 Dispose
    public static FileStreamResult AttachmentFile(this ControllerBase controller, AttachmentContent content)
    {
        controller.Response.Headers.XContentTypeOptions = "nosniff";
        return controller.File(content.Content, content.ContentType, content.FileName);
    }
}
