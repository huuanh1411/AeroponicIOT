using AeroponicIOT.Data;
using AeroponicIOT.DTOs;
using AeroponicIOT.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AeroponicIOT.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class GardenController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<GardenController> _logger;

    public GardenController(ApplicationDbContext context, ILogger<GardenController> logger)
    {
        _context = context;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> GetAllGardens()
    {
        try
        {
            var gardens = await _context.Gardens
                .Include(g => g.Devices)
                .ToListAsync();

            var dtos = gardens.Select(g => new GardenDto
            {
                Id = g.Id,
                Name = g.Name,
                Location = g.Location,
                Description = g.Description,
                CreatedAt = g.CreatedAt,
                DeviceCount = g.Devices?.Count ?? 0
            }).ToList();

            return Ok(dtos);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving gardens");
            return StatusCode(500, new { detail = "Error retrieving gardens" });
        }
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetGardenById(int id)
    {
        try
        {
            var garden = await _context.Gardens
                .Include(g => g.Devices)
                .FirstOrDefaultAsync(g => g.Id == id);

            if (garden == null)
            {
                return NotFound(new { detail = "Garden not found" });
            }

            var dto = new GardenDto
            {
                Id = garden.Id,
                Name = garden.Name,
                Location = garden.Location,
                Description = garden.Description,
                CreatedAt = garden.CreatedAt,
                DeviceCount = garden.Devices?.Count ?? 0
            };

            return Ok(dto);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving garden {GardenId}", id);
            return StatusCode(500, new { detail = "Error retrieving garden" });
        }
    }

    [HttpPost]
    public async Task<IActionResult> CreateGarden([FromBody] GardenDto dto)
    {
        try
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var garden = new Garden
            {
                Name = dto.Name,
                Location = dto.Location,
                Description = dto.Description,
                CreatedAt = DateTime.UtcNow
            };

            _context.Gardens.Add(garden);
            await _context.SaveChangesAsync();

            dto.Id = garden.Id;
            dto.CreatedAt = garden.CreatedAt;
            dto.DeviceCount = 0;

            return CreatedAtAction(nameof(GetGardenById), new { id = garden.Id }, dto);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating garden");
            return StatusCode(500, new { detail = "Error creating garden" });
        }
    }

    [HttpPut("{id}")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> UpdateGarden(int id, [FromBody] GardenDto dto)
    {
        try
        {
            var garden = await _context.Gardens.FindAsync(id);
            if (garden == null)
            {
                return NotFound(new { detail = "Garden not found" });
            }

            garden.Name = dto.Name;
            garden.Location = dto.Location;
            garden.Description = dto.Description;

            _context.Gardens.Update(garden);
            await _context.SaveChangesAsync();

            dto.Id = garden.Id;
            dto.CreatedAt = garden.CreatedAt;
            dto.DeviceCount = await _context.Devices.CountAsync(d => d.GardenId == garden.Id);

            return Ok(dto);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating garden {GardenId}", id);
            return StatusCode(500, new { detail = "Error updating garden" });
        }
    }

    [HttpDelete("{id}")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> DeleteGarden(int id)
    {
        try
        {
            var garden = await _context.Gardens
                .Include(g => g.Devices)
                .FirstOrDefaultAsync(g => g.Id == id);

            if (garden == null)
            {
                return NotFound(new { detail = "Garden not found" });
            }

            // Detach devices but do not delete them.
            foreach (var device in garden.Devices)
            {
                device.GardenId = null;
            }

            _context.Gardens.Remove(garden);
            await _context.SaveChangesAsync();

            return Ok(new { detail = "Garden deleted successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting garden {GardenId}", id);
            return StatusCode(500, new { detail = "Error deleting garden" });
        }
    }

    [HttpGet("{id}/devices")]
    public async Task<IActionResult> GetGardenDevices(int id)
    {
        try
        {
            var exists = await _context.Gardens.AnyAsync(g => g.Id == id);
            if (!exists)
            {
                return NotFound(new { detail = "Garden not found" });
            }

            var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            var userRole = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;

            var devicesQuery = _context.Devices.Where(d => d.GardenId == id).Include(d => d.Crop).AsQueryable();
            if (userRole != "Administrator")
            {
                if (!int.TryParse(userIdClaim, out var userIdInt))
                    return Unauthorized();

                devicesQuery = devicesQuery.Where(d => d.UserId == userIdInt);
            }

            var devices = await devicesQuery.ToListAsync();

            var dtos = devices.Select(d => new DeviceDto
            {
                Id = d.Id,
                Name = d.DeviceName ?? "Unknown Device",
                MacAddress = d.MacAddress,
                Status = d.Status,
                IsActive = d.IsActive,
                CurrentCropId = d.CurrentCropId,
                CropName = d.Crop?.Name,
                CreatedAt = d.CreatedAt,
                LastSeen = d.LastSeen
            }).ToList();

            return Ok(dtos);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving devices for garden {GardenId}", id);
            return StatusCode(500, new { detail = "Error retrieving garden devices" });
        }
    }
}

