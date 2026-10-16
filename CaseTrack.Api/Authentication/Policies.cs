namespace CaseTrack.Api.Authentication;

// 授權的第 1 層：哪些角色能做這一類動作（失敗 → 403）。
// 第 2 層「能碰哪一筆資料」在 Application 的 CaseAccessScope（失敗 → 404）。
// Policy 的名稱表達意圖，角色清單只寫在 Program.cs 一個地方；要調整誰能做什麼，不用改每個端點
public static class Policies
{
    public const string CaseReader = "CaseReader";            // 查詢案件：承辦人、主管、分派人員
    public const string CaseHandler = "CaseHandler";          // 處理案件（受理、補件、結案、退件）：承辦人、主管
    public const string CaseDispatcher = "CaseDispatcher";    // 分派、不予受理：分派人員
}
