using System.ComponentModel.DataAnnotations;

namespace CaseTrack.Api.Contracts;

// 長度跟資料庫一致：Code nvarchar(10)、Name nvarchar(50)。
// 代碼只允許英文字母與數字：會出現在報表、其他系統的對照裡，不要有空白或符號
public sealed record CreateDepartmentRequest(
    [Required(ErrorMessage = "代碼不可為空")]
    [MaxLength(10, ErrorMessage = "代碼長度不能超過 {1} 個字元")]
    [RegularExpression("^[A-Za-z0-9]+$", ErrorMessage = "代碼只能是英文字母或數字")]
    string Code,

    [Required(ErrorMessage = "名稱不可為空")]
    [MaxLength(50, ErrorMessage = "名稱長度不能超過 {1} 個字")]
    string Name);

public sealed record RenameDepartmentRequest(
    [Required(ErrorMessage = "名稱不可為空")]
    [MaxLength(50, ErrorMessage = "名稱長度不能超過 {1} 個字")]
    string Name);
