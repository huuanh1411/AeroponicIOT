using AeroponicIOT.Data;
using AeroponicIOT.DTOs;
using AeroponicIOT.Services.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AeroponicIOT.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = "AdminOnly")]
public class UsersController : ControllerBase
{
    private static readonly Dictionary<string, string> AllowedRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Farmer"] = "Farmer",
        ["Administrator"] = "Administrator"
    };

    private readonly ApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;
    private readonly ILogger<UsersController> _logger;

    public UsersController(
        ApplicationDbContext context,
        ICurrentUserService currentUserService,
        ILogger<UsersController> logger)
    {
        _context = context;
        _currentUserService = currentUserService;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> GetUsers()
    {
        try
        {
            var users = await _context.Users
                .AsNoTracking()
                .OrderBy(u => u.Username)
                .Select(u => new UserAdminListItemDto
                {
                    Id = u.Id,
                    Username = u.Username,
                    Email = u.Email,
                    Role = u.Role,
                    CreatedAt = u.CreatedAt,
                    LastLogin = u.LastLogin,
                    DeviceCount = u.Devices.Count
                })
                .ToListAsync();

            return Ok(ApiResponse.Success(users, "Users retrieved"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing users");
            return ApiProblem(StatusCodes.Status500InternalServerError, "Internal Server Error", "Error retrieving users");
        }
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetUser(int id)
    {
        try
        {
            var user = await _context.Users
                .AsNoTracking()
                .Where(u => u.Id == id)
                .Select(u => new UserAdminListItemDto
                {
                    Id = u.Id,
                    Username = u.Username,
                    Email = u.Email,
                    Role = u.Role,
                    CreatedAt = u.CreatedAt,
                    LastLogin = u.LastLogin,
                    DeviceCount = u.Devices.Count
                })
                .FirstOrDefaultAsync();

            if (user is null)
            {
                return ApiProblem(StatusCodes.Status404NotFound, "Not Found", "User not found");
            }

            return Ok(ApiResponse.Success(user, "User retrieved"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving user {UserId}", id);
            return ApiProblem(StatusCodes.Status500InternalServerError, "Internal Server Error", "Error retrieving user");
        }
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> UpdateUser(int id, [FromBody] UpdateUserAdminRequest request)
    {
        try
        {
            if (!ModelState.IsValid)
            {
                return ValidationProblem(ModelState);
            }

            if (!TryNormalizeRole(request.Role, out var normalizedRole))
            {
                return ApiProblem(
                    StatusCodes.Status400BadRequest,
                    "Bad Request",
                    "Invalid role. Allowed roles: Farmer, Administrator",
                    "invalid_role");
            }

            var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == id);
            if (user is null)
            {
                return ApiProblem(StatusCodes.Status404NotFound, "Not Found", "User not found");
            }

            var email = request.Email?.Trim();
            if (!string.IsNullOrWhiteSpace(email))
            {
                var emailTaken = await _context.Users
                    .AsNoTracking()
                    .AnyAsync(u => u.Email == email && u.Id != id);

                if (emailTaken)
                {
                    return ApiProblem(StatusCodes.Status400BadRequest, "Bad Request", "Email already exists", "email_exists");
                }

                user.Email = email;
            }

            var currentUser = _currentUserService.GetCurrentUser();
            if (currentUser.UserId == id
                && !string.Equals(normalizedRole, "Administrator", StringComparison.OrdinalIgnoreCase))
            {
                return ApiProblem(
                    StatusCodes.Status400BadRequest,
                    "Bad Request",
                    "You cannot remove your own administrator role",
                    "cannot_demote_self");
            }

            user.Role = normalizedRole;
            await _context.SaveChangesAsync();

            var dto = new UserAdminListItemDto
            {
                Id = user.Id,
                Username = user.Username,
                Email = user.Email,
                Role = user.Role,
                CreatedAt = user.CreatedAt,
                LastLogin = user.LastLogin,
                DeviceCount = await _context.Devices.CountAsync(d => d.UserId == user.Id)
            };

            _logger.LogInformation("User {UserId} updated by admin {AdminId}", id, currentUser.UserId);

            return Ok(ApiResponse.Success(dto, "User updated"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating user {UserId}", id);
            return ApiProblem(StatusCodes.Status500InternalServerError, "Internal Server Error", "Error updating user");
        }
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> DeleteUser(int id)
    {
        try
        {
            var currentUser = _currentUserService.GetCurrentUser();
            if (currentUser.UserId == id)
            {
                return ApiProblem(
                    StatusCodes.Status400BadRequest,
                    "Bad Request",
                    "You cannot delete your own account",
                    "cannot_delete_self");
            }

            var user = await _context.Users.FindAsync(id);
            if (user is null)
            {
                return ApiProblem(StatusCodes.Status404NotFound, "Not Found", "User not found");
            }

            var notifications = await _context.Notifications
                .Where(n => n.UserId == id)
                .ToListAsync();
            if (notifications.Count > 0)
            {
                _context.Notifications.RemoveRange(notifications);
            }

            _context.Users.Remove(user);
            await _context.SaveChangesAsync();

            _logger.LogInformation("User {UserId} deleted by admin {AdminId}", id, currentUser.UserId);

            return Ok(ApiResponse.Success<object?>(null, "User deleted successfully"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting user {UserId}", id);
            return ApiProblem(StatusCodes.Status500InternalServerError, "Internal Server Error", "Error deleting user");
        }
    }

    private ObjectResult ApiProblem(int statusCode, string title, string detail, string? errorCode = null)
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
}
