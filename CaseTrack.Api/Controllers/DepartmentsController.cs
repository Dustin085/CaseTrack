using CaseTrack.Application.Departments;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CaseTrack.Api.Controllers;

// 登入就能看，不限角色：機關的組織是公開資訊，不是個資。
// 分派人員需要下拉選單、承辦端的列表要顯示單位名稱，民眾之後的案件詳情也可能顯示承辦單位
[Authorize]
[ApiController]
[Route("api/departments")]
public sealed class DepartmentsController : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<DepartmentSummary>>> ListDepartments(
        [FromServices] ListDepartmentsHandler handler,
        CancellationToken cancellationToken)
    {
        return Ok(await handler.HandleAsync(cancellationToken));
    }
}
