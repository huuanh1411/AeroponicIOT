using System.ComponentModel.DataAnnotations;

namespace AeroponicIOT.DTOs;

/// <summary>
/// Request model for user login
/// </summary>
public class LoginRequest
{
    [Required]
    [StringLength(100, MinimumLength = 3)]
    public string? Username { get; set; }

    [Required]
    [StringLength(128, MinimumLength = 8)]
    public string? Password { get; set; }
}

/// <summary>
/// Request model for user registration
/// </summary>
public class RegisterRequest
{
    [Required]
    [StringLength(100, MinimumLength = 3)]
    public string? Username { get; set; }

    [Required]
    [EmailAddress]
    [StringLength(256)]
    public string? Email { get; set; }

    [Required]
    [StringLength(128, MinimumLength = 8)]
    [RegularExpression(@"^(?=.*[a-z])(?=.*[A-Z])(?=.*\d).{8,}$", ErrorMessage = "Password must be at least 8 characters and include upper, lower, and numeric characters")]
    public string? Password { get; set; }

    // Role is accepted in the DTO but will be ignored for unauthenticated requests.
    // Only an authenticated Administrator may assign a role on registration.
    [StringLength(50)]
    public string? Role { get; set; }
}

/// <summary>
/// Response model containing JWT token and user information
/// </summary>
public class TokenResponse
{
    public bool Success { get; set; }
    public string? Message { get; set; }
    public string? Token { get; set; }
    public string? Username { get; set; }
    public string? Role { get; set; }
    public int? UserId { get; set; }
    public DateTime? ExpiresAt { get; set; }
}

/// <summary>
/// Response model for user info
/// </summary>
public class UserInfoDto
{
    public int Id { get; set; }
    public string? Username { get; set; }
    public string? Email { get; set; }
    public string? Role { get; set; }
    public DateTime? CreatedAt { get; set; }
    public DateTime? LastLogin { get; set; }
}
