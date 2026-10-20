namespace CaseTrack.Api.Contracts;

// GET /api/me：後端實際認定的身分。前端用它決定畫面要顯示什麼，不自己解開 JWT。
// Roles 用 List 不用 Set：JSON 只有陣列，而且 System.Text.Json 讀不回 IReadOnlySet（不知道要建立哪一種 Set），
// 呼叫端（測試、之後的 C# 用戶端）會反序列化失敗
public sealed record MeResponse(
    string UserId,
    string? Name,
    IReadOnlyList<string> Roles,
    Guid? DepartmentId);
