using CodeDesigner.Ps2.WebApi.Dtos;
using CodeDesigner.Ps2.WebApi.Services;
using Microsoft.AspNetCore.Mvc;

namespace CodeDesigner.Ps2.WebApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly AuthService _authService;

    public AuthController(AuthService authService)
    {
        _authService = authService;
    }

    [HttpPost("login")]
    public IActionResult Login([FromBody] LoginRequest request)
    {
        var (success, token, expiresAt, error) = _authService.Login(request.Username, request.Password);
        if (!success)
            return Unauthorized(ApiResponse<AuthResponse>.Fail(error!));

        return Ok(ApiResponse<AuthResponse>.Ok(new AuthResponse
        {
            Token = token!,
            Username = request.Username,
            ExpiresAt = expiresAt
        }));
    }

    [HttpPost("register")]
    public IActionResult Register([FromBody] RegisterRequest request)
    {
        var (success, error) = _authService.Register(request.Username, request.Email, request.Password);
        if (!success)
            return BadRequest(ApiResponse<string>.Fail(error!));

        // Auto-login after registration
        var (loginSuccess, token, expiresAt, loginError) = _authService.Login(request.Username, request.Password);
        if (!loginSuccess)
            return StatusCode(500, ApiResponse<AuthResponse>.Fail("Registration succeeded but login failed"));

        return Ok(ApiResponse<AuthResponse>.Ok(new AuthResponse
        {
            Token = token!,
            Username = request.Username,
            ExpiresAt = expiresAt
        }));
    }
}
