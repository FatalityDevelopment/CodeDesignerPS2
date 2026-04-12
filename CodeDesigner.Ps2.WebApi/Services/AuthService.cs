using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace CodeDesigner.Ps2.WebApi.Services;

public class AuthService
{
    private readonly IConfiguration _config;

    // In-memory user store for now — replace with DB-backed store later
    private static readonly Dictionary<string, (string PasswordHash, string Salt, string Email)> Users = new();

    public AuthService(IConfiguration config)
    {
        _config = config;
    }

    public (bool Success, string? Error) Register(string username, string email, string password)
    {
        if (Users.ContainsKey(username.ToLowerInvariant()))
            return (false, "Username already exists");

        var salt = GenerateSalt();
        var hash = HashPassword(password, salt);
        Users[username.ToLowerInvariant()] = (hash, salt, email);
        return (true, null);
    }

    public (bool Success, string? Token, DateTime ExpiresAt, string? Error) Login(string username, string password)
    {
        if (!Users.TryGetValue(username.ToLowerInvariant(), out var user))
            return (false, null, default, "Invalid username or password");

        var hash = HashPassword(password, user.Salt);
        if (hash != user.PasswordHash)
            return (false, null, default, "Invalid username or password");

        var expiresAt = DateTime.UtcNow.AddHours(24);
        var token = GenerateToken(username, expiresAt);
        return (true, token, expiresAt, null);
    }

    private string GenerateToken(string username, DateTime expiresAt)
    {
        var key = _config["Jwt:Key"] ?? "CodeDesigner-Dev-Secret-Key-Change-In-Production-32chars!";
        var issuer = _config["Jwt:Issuer"] ?? "CodeDesigner.Ps2.WebApi";

        var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key));
        var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(ClaimTypes.Name, username),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: issuer,
            claims: claims,
            expires: expiresAt,
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static string GenerateSalt()
    {
        var saltBytes = new byte[32];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(saltBytes);
        return Convert.ToBase64String(saltBytes);
    }

    private static string HashPassword(string password, string salt)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(salt));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(password));
        return Convert.ToBase64String(hash);
    }
}
