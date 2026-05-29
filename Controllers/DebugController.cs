using AeroponicIOT.Data;
using AeroponicIOT.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AeroponicIOT.Controllers;

[ApiController]
[Route("api/[controller]")]
public class DebugController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly IWebHostEnvironment _env;

    public DebugController(ApplicationDbContext db, IWebHostEnvironment env)
    {
        _db = db;
        _env = env;
    }

    /// <summary>
    /// Create a local development test user. Only works when ASPNETCORE_ENVIRONMENT=Development.
    /// Returns the created username and password.
    /// </summary>
    [HttpPost("create-test-user")]
    public async Task<IActionResult> CreateTestUser()
    {
        if (!_env.IsDevelopment())
        {
            return Forbid();
        }

        var username = "devuser";
        var password = "P@ssw0rd1"; // meets validation: 8+ chars, upper/lower/digit

        // ensure user doesn't already exist
        var exists = await _db.Users.AsNoTracking().AnyAsync(u => u.Username == username);
        if (exists)
        {
            return Ok(new { message = "Test user already exists", username });
        }

        var user = new User
        {
            Username = username,
            Email = "devuser@example.local",
            Role = "Administrator",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
            CreatedAt = DateTime.UtcNow
        };

        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        return Ok(new { message = "Test user created", username, password });
    }
}
