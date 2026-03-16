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
public class CropController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<CropController> _logger;

    public CropController(ApplicationDbContext context, ILogger<CropController> logger)
    {
        _context = context;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> GetAllCrops()
    {
        try
        {
            var crops = await _context.Crops
                .Include(c => c.CropStages)
                .ToListAsync();

            var cropDtos = crops.Select(MapCropListItem).ToList();

            return Ok(cropDtos);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting crops");
            return StatusCode(500, new { detail = "Error retrieving crops" });
        }
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetCropById(int id)
    {
        try
        {
            var crop = await _context.Crops
                .Include(c => c.CropStages)
                .Include(c => c.Devices)
                .FirstOrDefaultAsync(c => c.Id == id);

            if (crop == null)
            {
                _logger.LogWarning("Crop {CropId} not found", id);
                return NotFound(new { detail = "Crop not found" });
            }

            var cropDto = new
            {
                crop.Id,
                crop.Name,
                crop.Description,
                crop.TotalDaysEst,
                crop.CreatedAt,
                Stages = crop.CropStages?.Select(s => new { 
                    s.Id, 
                    s.StageName, 
                    s.DayStart,
                    s.DayEnd,
                    s.PhMin,
                    s.PhMax,
                    s.PpmMin,
                    s.PpmMax,
                    s.WaterTempMin,
                    s.WaterTempMax,
                    s.HumidityMin,
                    s.HumidityMax,
                    s.PumpOnMinutes,
                    s.PumpOffMinutes
                }).ToList(),
                DeviceCount = crop.Devices?.Count ?? 0
            };

            return Ok(cropDto);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting crop {CropId}", id);
            return StatusCode(500, new { detail = "Error retrieving crop" });
        }
    }

    [HttpPost]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> CreateCrop([FromBody] CropUpsertDto dto)
    {
        try
        {
            var validationError = ValidateCrop(dto);
            if (validationError != null)
            {
                return BadRequest(new { detail = validationError });
            }

            var crop = new Crop
            {
                Name = dto.Name.Trim(),
                Description = string.IsNullOrWhiteSpace(dto.Description) ? null : dto.Description.Trim(),
                TotalDaysEst = dto.TotalDaysEst,
                CreatedAt = DateTime.UtcNow,
                CropStages = dto.Stages
                    .OrderBy(s => s.DayStart ?? int.MaxValue)
                    .Select(MapStage)
                    .ToList()
            };

            _context.Crops.Add(crop);
            await _context.SaveChangesAsync();

            return CreatedAtAction(nameof(GetCropById), new { id = crop.Id }, MapCropListItem(crop));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating crop");
            return StatusCode(500, new { detail = "Error creating crop" });
        }
    }

    [HttpPut("{id}")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> UpdateCrop(int id, [FromBody] CropUpsertDto dto)
    {
        try
        {
            var crop = await _context.Crops
                .Include(c => c.CropStages)
                .FirstOrDefaultAsync(c => c.Id == id);

            if (crop == null)
            {
                return NotFound(new { detail = "Crop not found" });
            }

            var validationError = ValidateCrop(dto);
            if (validationError != null)
            {
                return BadRequest(new { detail = validationError });
            }

            crop.Name = dto.Name.Trim();
            crop.Description = string.IsNullOrWhiteSpace(dto.Description) ? null : dto.Description.Trim();
            crop.TotalDaysEst = dto.TotalDaysEst;

            _context.CropStages.RemoveRange(crop.CropStages);
            crop.CropStages = dto.Stages
                .OrderBy(s => s.DayStart ?? int.MaxValue)
                .Select(MapStage)
                .ToList();

            await _context.SaveChangesAsync();

            return Ok(MapCropListItem(crop));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating crop {CropId}", id);
            return StatusCode(500, new { detail = "Error updating crop" });
        }
    }

    [HttpDelete("{id}")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> DeleteCrop(int id)
    {
        try
        {
            var crop = await _context.Crops
                .Include(c => c.Devices)
                .Include(c => c.CropStages)
                .FirstOrDefaultAsync(c => c.Id == id);

            if (crop == null)
            {
                return NotFound(new { detail = "Crop not found" });
            }

            if (crop.Devices.Any())
            {
                return BadRequest(new { detail = "Cannot delete a crop that is currently assigned to devices" });
            }

            _context.CropStages.RemoveRange(crop.CropStages);
            _context.Crops.Remove(crop);
            await _context.SaveChangesAsync();

            return Ok(new { detail = "Crop deleted successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting crop {CropId}", id);
            return StatusCode(500, new { detail = "Error deleting crop" });
        }
    }

    private static object MapCropListItem(Crop crop)
    {
        return new
        {
            crop.Id,
            crop.Name,
            crop.Description,
            crop.TotalDaysEst,
            crop.CreatedAt,
            StageCount = crop.CropStages?.Count ?? 0
        };
    }

    private static CropStage MapStage(CropStageUpsertDto stage)
    {
        return new CropStage
        {
            StageName = stage.StageName.Trim(),
            DayStart = stage.DayStart,
            DayEnd = stage.DayEnd,
            PhMin = stage.PhMin,
            PhMax = stage.PhMax,
            PpmMin = stage.PpmMin,
            PpmMax = stage.PpmMax,
            WaterTempMin = stage.WaterTempMin,
            WaterTempMax = stage.WaterTempMax,
            HumidityMin = stage.HumidityMin,
            HumidityMax = stage.HumidityMax,
            PumpOnMinutes = stage.PumpOnMinutes,
            PumpOffMinutes = stage.PumpOffMinutes
        };
    }

    private static string? ValidateCrop(CropUpsertDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Name))
        {
            return "Crop name is required";
        }

        if (dto.Stages.Count == 0)
        {
            return "At least one crop stage is required";
        }

        var orderedStages = dto.Stages.OrderBy(s => s.DayStart ?? int.MaxValue).ToList();
        var previousEnd = 0;

        foreach (var stage in orderedStages)
        {
            if (string.IsNullOrWhiteSpace(stage.StageName))
            {
                return "Each stage must have a name";
            }

            if (!stage.DayStart.HasValue || !stage.DayEnd.HasValue)
            {
                return "Each stage must define both start day and end day";
            }

            if (stage.DayStart.Value <= 0 || stage.DayEnd.Value < stage.DayStart.Value)
            {
                return "Stage day ranges are invalid";
            }

            if (stage.DayStart.Value <= previousEnd)
            {
                return "Stage day ranges must be ordered and non-overlapping";
            }

            previousEnd = stage.DayEnd.Value;
        }

        return null;
    }
}
