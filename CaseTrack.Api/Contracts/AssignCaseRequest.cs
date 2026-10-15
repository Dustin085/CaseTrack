using System.ComponentModel.DataAnnotations;

namespace CaseTrack.Api.Contracts;

public sealed record AssignCaseRequest(
    [Required(ErrorMessage = "單位不可為空")]
    Guid? DepartmentId);