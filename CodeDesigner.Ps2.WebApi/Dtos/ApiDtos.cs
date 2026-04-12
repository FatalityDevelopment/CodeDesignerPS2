namespace CodeDesigner.Ps2.WebApi.Dtos;

public class LoginRequest
{
    public required string Username { get; set; }
    public required string Password { get; set; }
}

public class RegisterRequest
{
    public required string Username { get; set; }
    public required string Email { get; set; }
    public required string Password { get; set; }
}

public class AuthResponse
{
    public required string Token { get; set; }
    public required string Username { get; set; }
    public DateTime ExpiresAt { get; set; }
}

public class ProjectDto
{
    public int Id { get; set; }
    public required string Title { get; set; }
    public string? Description { get; set; }
    public string? ImageUrl { get; set; }
    public string? Url { get; set; }
}

public class CheatListDto
{
    public int Id { get; set; }
    public required string Title { get; set; }
    public string? GameTitle { get; set; }
    public int CheatCount { get; set; }
}

public class CheatDto
{
    public int Id { get; set; }
    public required string Label { get; set; }
    public string? Description { get; set; }
    public string? Author { get; set; }
    public bool Enabled { get; set; }
    public List<string> Lines { get; set; } = new();
}

public class ResearchEntryDto
{
    public int Id { get; set; }
    public required string Title { get; set; }
    public string? Content { get; set; }
    public string? Author { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class ServerStatusDto
{
    public required string Name { get; set; }
    public required string Status { get; set; }
    public int PlayersOnline { get; set; }
    public string? GameTitle { get; set; }
    public string? Region { get; set; }
}

public class ApiResponse<T>
{
    public bool Success { get; set; }
    public T? Data { get; set; }
    public string? Error { get; set; }

    public static ApiResponse<T> Ok(T data) => new() { Success = true, Data = data };
    public static ApiResponse<T> Fail(string error) => new() { Success = false, Error = error };
}
