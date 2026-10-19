using System.ComponentModel.DataAnnotations;

namespace CaseTrack.Api.Contracts;

public sealed record CloseCaseRequest(
    [Required(ErrorMessage = "結案回覆不能為空")]
    [MaxLength(2000, ErrorMessage = "結案回覆長度不能超過 2000 個字元")]
    string Reply);
