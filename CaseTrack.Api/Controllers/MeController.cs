using CaseTrack.Api.Authentication;
using CaseTrack.Api.Contracts;
using CaseTrack.Application.Abstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CaseTrack.Api.Controllers;

// 目前登入的是誰。前端不解開 Access Token：它是給 API 看的，有些 IdP 的格式前端根本解不開，
// 而且之後加了 IClaimsTransformation，token 裡的資料跟後端認定的就不一樣了。
// 沒有業務邏輯，只是把身分讀出來，所以不經過 Application 的 Handler
[Authorize]
[ApiController]
[Route("api/me")]
public sealed class MeController : ControllerBase
{
    [HttpGet]
    public ActionResult<MeResponse> GetMe([FromServices] ICurrentUser currentUser)
    {
        // 角色、單位從 ICurrentUser 取得（授權實際用的值）；姓名只用來顯示，Application 用不到，直接讀 claim。
        // 要用 .Value：Claim.ToString() 是 "name: 王小明"
        return Ok(new MeResponse(
            currentUser.UserId,
            User.FindFirst(CaseTrackClaimTypes.Name)?.Value,
            currentUser.Roles.Order(StringComparer.Ordinal).ToList(),     // 排序：輸出的順序固定
            currentUser.DepartmentId));
    }
}
