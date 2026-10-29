using System.Text.Json;
using CaseTrack.Application.Common;
using CaseTrack.Application.Exceptions;

namespace CaseTrack.Application.Auditing;

// 稽核紀錄查詢（管理者）。日期是台灣的日期、含頭含尾，都可以不填
public sealed record AuditLogQuery(
    string? EntityType,
    string? EntityId,
    string? ChangedBy,
    DateOnly? From,
    DateOnly? To,
    int Page,
    int PageSize)
{
    public const int DefaultPageSize = 50;
    public const int MaxPageSize = 100;
}

// 資料庫裡的原始紀錄：Changes 是 AuditInterceptor 寫入的 JSON（{"欄位": {"Old": ..., "New": ...}}）
public sealed record AuditRecord(
    long Id,
    string EntityType,
    string EntityId,
    string Action,
    string Changes,
    string ChangedBy,
    DateTimeOffset ChangedAt);

// 回傳給管理者的一筆。Hidden = true 時只知道這個欄位被改了，看不到值
public sealed record AuditFieldChange(string Field, string? Old, string? New, bool Hidden);

public sealed record AuditEntrySummary(
    long Id,
    string EntityType,
    string EntityId,
    string Action,
    IReadOnlyList<AuditFieldChange> Changes,
    string ChangedBy,
    DateTimeOffset ChangedAt);

public interface IAuditLogQuery
{
    // 依時間新到舊。時間範圍是 [from, to)，null 代表不限
    Task<PagedResult<AuditRecord>> ListAsync(
        string? entityType, string? entityId, string? changedBy,
        DateTimeOffset? from, DateTimeOffset? to, int page, int pageSize,
        CancellationToken cancellationToken);
}

// 管理者看得到值的欄位（白名單）。管理者不能看案件內容：主旨、內容、回覆、原因、檔名、民眾 Id 都不在名單上，
// 只顯示「這個欄位被改了」。用白名單不用黑名單：之後新增的欄位預設看不到，忘了更新名單只是少顯示，不會外洩
public static class AuditFieldPolicy
{
    private static readonly Dictionary<string, HashSet<string>> VisibleFields = new()
    {
        ["Case"] = ["Id", "CaseNumber", "Status", "DepartmentId", "SubmittedAt", "ClosingSubmittedBy"],
        ["SupplementRequest"] = ["Id", "CaseId", "Sequence", "RequestedAt", "SubmittedAt"],
        ["Attachment"] = ["Id", "CaseId", "SupplementRequestId", "ContentType", "Size", "UploadedAt"],
        ["Department"] = ["Id", "Code", "Name", "IsActive"],
    };

    public static bool IsVisible(string entityType, string field) =>
        VisibleFields.TryGetValue(entityType, out var fields) && fields.Contains(field);
}

public sealed class AuditLogHandler
{
    private static readonly TimeSpan TaiwanOffset = TimeSpan.FromHours(8);

    private readonly IAuditLogQuery _query;

    public AuditLogHandler(IAuditLogQuery query)
    {
        _query = query;
    }

    public async Task<PagedResult<AuditEntrySummary>> HandleAsync(AuditLogQuery query, CancellationToken cancellationToken)
    {
        if (query.From is { } f && query.To is { } t && f > t)
        {
            throw new RequestValidationException("From", "開始日期不能晚於結束日期");
        }

        // 跟報表一樣：結束日用「隔天 0 點、不含」
        DateTimeOffset? start = query.From is { } from
            ? new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue), TaiwanOffset)
            : null;
        DateTimeOffset? end = query.To is { } to
            ? new DateTimeOffset(to.AddDays(1).ToDateTime(TimeOnly.MinValue), TaiwanOffset)
            : null;

        var result = await _query.ListAsync(
            Blank(query.EntityType), Blank(query.EntityId), Blank(query.ChangedBy),
            start, end, query.Page, query.PageSize, cancellationToken);

        return new PagedResult<AuditEntrySummary>(
            result.Items.Select(ToSummary).ToList(), result.Page, result.PageSize, result.TotalCount);
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static AuditEntrySummary ToSummary(AuditRecord record) => new(
        record.Id,
        record.EntityType,
        record.EntityId,
        record.Action,
        ParseChanges(record.EntityType, record.Changes),
        record.ChangedBy,
        record.ChangedAt);

    // 白名單以外的欄位，值在這裡就丟掉，不會出現在 API 回應裡（不是靠前端隱藏）
    private static List<AuditFieldChange> ParseChanges(string entityType, string changes)
    {
        using var document = JsonDocument.Parse(changes);
        return document.RootElement.EnumerateObject()
            .Select(field => AuditFieldPolicy.IsVisible(entityType, field.Name)
                ? new AuditFieldChange(field.Name, ToText(field.Value, "Old"), ToText(field.Value, "New"), Hidden: false)
                : new AuditFieldChange(field.Name, null, null, Hidden: true))
            .ToList();
    }

    // 字串取原值（不要帶引號），其他型別（數字、布林）用 JSON 的寫法
    private static string? ToText(JsonElement change, string name) =>
        change.TryGetProperty(name, out var value)
            ? value.ValueKind switch
            {
                JsonValueKind.Null => null,
                JsonValueKind.String => value.GetString(),
                _ => value.GetRawText(),
            }
            : null;
}
