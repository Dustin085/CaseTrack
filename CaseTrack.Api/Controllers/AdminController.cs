using CaseTrack.Api.Authentication;
using CaseTrack.Api.Contracts;
using CaseTrack.Application.Auditing;
using CaseTrack.Application.Common;
using CaseTrack.Application.Departments;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CaseTrack.Api.Controllers;

// 系統管理：單位維護、稽核紀錄查詢。只有管理者（反過來，管理者也只能用這裡，案件的端點都是 403）
[Authorize(Policy = Policies.Administrator)]
[ApiController]
[Route("api/admin")]
public sealed class AdminController : ControllerBase
{
    // 包含停用的單位；跟 GET api/departments 是同一份資料，分開是為了之後管理畫面要的欄位可以各自調整
    [HttpGet("departments")]
    public async Task<ActionResult<IReadOnlyList<DepartmentSummary>>> ListDepartments(
        [FromServices] ListDepartmentsHandler handler,
        CancellationToken cancellationToken)
    {
        return Ok(await handler.HandleAsync(cancellationToken));
    }

    [HttpPost("departments")]
    public async Task<ActionResult<DepartmentSummary>> CreateDepartment(
        CreateDepartmentRequest request,
        [FromServices] ManageDepartmentsHandler handler,
        CancellationToken cancellationToken)
    {
        var department = await handler.CreateAsync(request.Code, request.Name, cancellationToken);
        // 沒有「查單一單位」的端點，Location 指向列表
        return Created("/api/admin/departments", department);
    }

    [HttpPut("departments/{id:guid}/name")]
    public async Task<IActionResult> RenameDepartment(
        Guid id,
        RenameDepartmentRequest request,
        [FromServices] ManageDepartmentsHandler handler,
        CancellationToken cancellationToken)
    {
        await handler.RenameAsync(id, request.Name, cancellationToken);
        return NoContent();
    }

    // 停用、啟用用兩個名詞資源，跟案件的 closing-approval 一樣不用「PATCH isActive」：意圖清楚，稽核也好讀
    [HttpPost("departments/{id:guid}/deactivation")]
    public async Task<IActionResult> DeactivateDepartment(
        Guid id,
        [FromServices] ManageDepartmentsHandler handler,
        CancellationToken cancellationToken)
    {
        await handler.SetActiveAsync(id, false, cancellationToken);
        return NoContent();
    }

    [HttpPost("departments/{id:guid}/activation")]
    public async Task<IActionResult> ActivateDepartment(
        Guid id,
        [FromServices] ManageDepartmentsHandler handler,
        CancellationToken cancellationToken)
    {
        await handler.SetActiveAsync(id, true, cancellationToken);
        return NoContent();
    }

    // 稽核紀錄只能查，沒有修改、刪除的端點
    [HttpGet("audit-entries")]
    public async Task<ActionResult<PagedResult<AuditEntrySummary>>> ListAuditEntries(
        [FromQuery] ListAuditEntriesRequest request,
        [FromServices] AuditLogHandler handler,
        CancellationToken cancellationToken)
    {
        var query = new AuditLogQuery(
            request.EntityType, request.EntityId, request.ChangedBy,
            request.From, request.To, request.Page, request.PageSize);
        return Ok(await handler.HandleAsync(query, cancellationToken));
    }
}
