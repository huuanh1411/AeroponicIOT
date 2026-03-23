namespace AeroponicIOT.DTOs;

public class ApiResponse<T>
{
    public bool Success { get; init; } = true;
    public string Message { get; init; } = "Success";
    public T? Data { get; init; }
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
}

public static class ApiResponse
{
    public static ApiResponse<T> Success<T>(T? data, string message = "Success") => new()
    {
        Success = true,
        Message = message,
        Data = data,
        Timestamp = DateTime.UtcNow
    };
}
