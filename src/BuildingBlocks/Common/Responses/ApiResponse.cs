namespace Hdos.Common.Responses;

public sealed record ApiResponse<T>(
    bool Success,
    T? Data,
    string? ErrorCode,
    string? ErrorMessage)
{
    public static ApiResponse<T> Ok(T data) => new(true, data, null, null);
    public static ApiResponse<T> Fail(string code, string message) => new(false, default, code, message);
}

public sealed record ApiResponse(
    bool Success,
    string? ErrorCode,
    string? ErrorMessage)
{
    public static ApiResponse Ok() => new(true, null, null);
    public static ApiResponse Fail(string code, string message) => new(false, code, message);
}
