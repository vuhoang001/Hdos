namespace Hdos.DynamicFormService.Domain.ValueObjects;

public sealed record FormSettings(
    string SubmitButtonLabel      = "Gửi",
    string SuccessMessage         = "Đã gửi form thành công",
    bool   AllowMultipleSubmissions = true);
