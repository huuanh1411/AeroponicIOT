using AeroponicIOT.Data;
using AeroponicIOT.DTOs;
using AeroponicIOT.Models;
using AeroponicIOT.Options;
using AeroponicIOT.Services.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Globalization;
using System.Security.Claims;
using System.Text;

namespace AeroponicIOT.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthenticationController : ControllerBase
{
    private static readonly Dictionary<string, string> AllowedRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Farmer"] = "Farmer",
        ["Administrator"] = "Administrator"
    };

    private readonly ApplicationDbContext _context;
    private readonly JwtSettingsOptions _jwtSettings;
    private readonly ILogger<AuthenticationController> _logger;
    private readonly ICurrentUserService _currentUserService;

    public AuthenticationController(
        ApplicationDbContext context,
        IOptions<JwtSettingsOptions> jwtSettings,
        ILogger<AuthenticationController> logger,
        ICurrentUserService currentUserService)
    {
        _context = context;
        _jwtSettings = jwtSettings.Value;
        _logger = logger;
        _currentUserService = currentUserService;
    }

    /// <summary>
    /// Register a new user
    /// </summary>
    [HttpPost("register")]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request)
    {
        try
        {
            var username = request.Username!.Trim();
            var email = request.Email?.Trim();

            // Check if user already exists
            var existingUser = await _context.Users
                .AsNoTracking()
                .AnyAsync(u => u.Username == username);

            if (existingUser)
            {
                return ApiProblem(StatusCodes.Status400BadRequest, "Bad Request", "Username already exists", "username_exists");
            }

            if (!string.IsNullOrWhiteSpace(email))
            {
                var existingEmail = await _context.Users
                    .AsNoTracking()
                    .AnyAsync(u => u.Email == email);

                if (existingEmail)
                {
                    return ApiProblem(StatusCodes.Status400BadRequest, "Bad Request", "Email already exists", "email_exists");
                }
            }

            // Determine role server-side. Only an authenticated Administrator may assign roles.
            var roleToAssign = "Farmer";
            if (_currentUserService.GetCurrentUser().IsAdministrator && !string.IsNullOrWhiteSpace(request.Role))
            {
                if (!TryNormalizeRole(request.Role, out var normalizedRole))
                {
                    return ApiProblem(StatusCodes.Status400BadRequest, "Bad Request", "Invalid role. Allowed roles: Farmer, Administrator", "invalid_role");
                }

                roleToAssign = normalizedRole;
            }

            // Create new user
            var user = new User
            {
                Username = username,
                Email = email,
                Role = roleToAssign,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
                CreatedAt = DateTime.UtcNow
            };

            _context.Users.Add(user);
            await _context.SaveChangesAsync();

            _logger.LogInformation("User {Username} registered successfully with role {Role}", 
                user.Username, user.Role);

            var legacyResponse = new TokenResponse
            {
                Success = true,
                Message = "User registered successfully",
                Username = user.Username,
                Role = user.Role,
                UserId = user.Id
            };

            var payload = new
            {
                username = user.Username,
                role = user.Role,
                userId = user.Id
            };

            return AuthSuccess(legacyResponse, payload, "User registered successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error registering user");
            return ApiProblem(StatusCodes.Status500InternalServerError, "Internal Server Error", "Internal server error");
        }
    }

    /// <summary>
    /// Login user and return JWT token
    /// </summary>
    [HttpPost("login")]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        try
        {
            // Find user by username
            var username = request.Username!.Trim();
            var user = await _context.Users
                .FirstOrDefaultAsync(u => u.Username == username);

            if (user == null || !BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
            {
                _logger.LogWarning("Login failed for username {Username}", request.Username);
                return ApiProblem(StatusCodes.Status401Unauthorized, "Unauthorized", "Invalid username or password", "invalid_credentials");
            }

            // Update last login
            user.LastLogin = DateTime.UtcNow;
            _context.Users.Update(user);
            await _context.SaveChangesAsync();

            // Generate JWT token
            var token = GenerateJwtToken(user);
            var expiresAt = DateTime.UtcNow.AddMinutes(_jwtSettings.ExpirationMinutes);

            _logger.LogInformation("User {Username} logged in successfully", user.Username);

            var legacyResponse = new TokenResponse
            {
                Success = true,
                Message = "Login successful",
                Token = token,
                Username = user.Username,
                Role = user.Role,
                UserId = user.Id,
                ExpiresAt = expiresAt
            };

            var payload = new
            {
                token,
                username = user.Username,
                role = user.Role,
                userId = user.Id,
                expiresAt
            };

            return AuthSuccess(legacyResponse, payload, "Login successful");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during login");
            return ApiProblem(StatusCodes.Status500InternalServerError, "Internal Server Error", "Internal server error");
        }
    }

    /// <summary>
    /// Get current user info (requires authentication)
    /// </summary>
    [HttpGet("me")]
    [Authorize]
    public async Task<IActionResult> GetCurrentUser()
    {
        try
        {
            var currentUser = _currentUserService.GetCurrentUser();
            if (!currentUser.UserId.HasValue)
            {
                return ApiProblem(StatusCodes.Status401Unauthorized, "Unauthorized", "User not authenticated");
            }

            var user = await _context.Users.FindAsync(currentUser.UserId.Value);
            if (user == null)
            {
                return ApiProblem(StatusCodes.Status404NotFound, "Not Found", "User not found");
            }

            var userInfo = new UserInfoDto
            {
                Id = user.Id,
                Username = user.Username,
                Email = user.Email,
                Role = user.Role,
                CreatedAt = user.CreatedAt,
                LastLogin = user.LastLogin
            };

            if (UseLegacyAuthResponse())
            {
                return Ok(userInfo);
            }

            return Ok(ApiResponse.Success(userInfo, "Current user retrieved"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving current user");
            return ApiProblem(StatusCodes.Status500InternalServerError, "Internal Server Error", "Error retrieving current user");
        }
    }

    private IActionResult ApiProblem(int statusCode, string title, string detail, string? errorCode = null)
    {
        return ProblemResponseFactory.Create(this, statusCode, title, detail, errorCode);
    }

    private static bool TryNormalizeRole(string? role, out string normalizedRole)
    {
        normalizedRole = string.Empty;
        if (string.IsNullOrWhiteSpace(role))
        {
            return false;
        }

        return AllowedRoles.TryGetValue(role.Trim(), out normalizedRole!);
    }

    private IActionResult AuthSuccess<TLegacy, TData>(TLegacy legacyPayload, TData data, string message)
    {
        if (UseLegacyAuthResponse())
        {
            return Ok(legacyPayload);
        }

        return Ok(ApiResponse.Success(data, message));
    }

    private bool UseLegacyAuthResponse()
    {
        var queryFlag = Request.Query["legacyAuthResponse"].FirstOrDefault();
        var headerFlag = Request.Headers["X-Legacy-Auth-Response"].FirstOrDefault();

        return IsTruthy(queryFlag) || IsTruthy(headerFlag);
    }

    private static bool IsTruthy(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return value.Equals("true", StringComparison.OrdinalIgnoreCase)
            || value.Equals("1", StringComparison.OrdinalIgnoreCase)
            || value.Equals("yes", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Generate JWT token for user
    /// </summary>
    private string GenerateJwtToken(User user)
    {
        var key = Encoding.ASCII.GetBytes(_jwtSettings.SecretKey);
        var tokenHandler = new JwtSecurityTokenHandler();

        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString(CultureInfo.InvariantCulture)),
            new Claim(ClaimTypes.Name, user.Username ?? ""),
            new Claim(ClaimTypes.Email, user.Email ?? ""),
            new Claim(ClaimTypes.Role, user.Role ?? "Farmer")
        };

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.AddMinutes(_jwtSettings.ExpirationMinutes),
            Issuer = _jwtSettings.Issuer,
            Audience = _jwtSettings.Audience,
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(key),
                SecurityAlgorithms.HmacSha256Signature)
        };

        var token = tokenHandler.CreateToken(tokenDescriptor);
        return tokenHandler.WriteToken(token);
    }
}
