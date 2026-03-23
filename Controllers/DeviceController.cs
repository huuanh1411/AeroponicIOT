using AeroponicIOT.Data;
using AeroponicIOT.DTOs;
using AeroponicIOT.Models;
using AeroponicIOT.Options;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Security.Claims;

namespace AeroponicIOT.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class DeviceController : ControllerBase
{
    private static readonly TimeSpan FailedAttemptWindow = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan FailedAttemptCooldown = TimeSpan.FromMinutes(2);
    private const int FailedAttemptThreshold = 5;
    private static readonly ConcurrentDictionary<string, OnboardingAttemptState> FailedOnboardingAttempts = new();

    private readonly ApplicationDbContext _context;
    private readonly ILogger<DeviceController> _logger;
    private readonly ProvisioningOptions _provisioningOptions;

    public DeviceController(
        ApplicationDbContext context,
        ILogger<DeviceController> logger,
        IOptions<ProvisioningOptions> provisioningOptions)
    {
        _context = context;
        _logger = logger;
        _provisioningOptions = provisioningOptions.Value;
    }

    private int GetCurrentUserId()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? User.FindFirst("sub")?.Value;
        if (int.TryParse(userIdClaim, out var userId))
        {
            return userId;
        }
        throw new UnauthorizedAccessException("User ID not found in token");
    }

    private string? GetCurrentUserRole()
    {
        return User.FindFirst(ClaimTypes.Role)?.Value
            ?? User.FindFirst("role")?.Value;
    }

    [HttpGet]
    public async Task<IActionResult> GetAllDevices()
    {
        try
        {
            var userId = GetCurrentUserId();
            var userRole = GetCurrentUserRole();

            IEnumerable<Device> devices;

            // Admins can see all devices, farmers only their own
            if (userRole == "Administrator")
            {
                devices = await _context.Devices
                    .Include(d => d.Crop)
                    .Include(d => d.Garden)
                    .ToListAsync();
            }
            else
            {
                devices = await _context.Devices
                    .Where(d => d.UserId == userId)
                    .Include(d => d.Crop)
                    .Include(d => d.Garden)
                    .ToListAsync();
            }

            var deviceDtos = devices.Select(d => new DeviceDto
            {
                Id = d.Id,
                Name = d.DeviceName ?? "Unknown Device",
                MacAddress = d.MacAddress,
                Status = d.Status,
                IsActive = d.IsActive,
                CurrentCropId = d.CurrentCropId,
                CropName = d.Crop?.Name,
                CropAssignedAt = d.CropAssignedAt,
                GardenId = d.GardenId,
                GardenName = d.Garden?.Name,
                CreatedAt = d.CreatedAt,
                LastSeen = d.LastSeen
            }).ToList();

            _logger.LogInformation("User {UserId} retrieved {Count} devices", userId, deviceDtos.Count);
            return Ok(ApiResponse.Success(deviceDtos, "Devices retrieved"));
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "Unauthorized while getting devices");
            return ApiProblem(StatusCodes.Status401Unauthorized, "Unauthorized", "User not authenticated");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting devices");
            return ApiProblem(StatusCodes.Status500InternalServerError, "Internal Server Error", "Error retrieving devices");
        }
    }

    [HttpGet("pending")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> GetPendingDevices()
    {
        try
        {
            var userId = GetCurrentUserId();
            var pendingDevices = await _context.Devices
                .Where(d => d.UserId == null)
                .OrderByDescending(d => d.LastSeen)
                .Select(d => new PendingDeviceDto
                {
                    Id = d.Id,
                    Name = d.DeviceName ?? "Pending Device",
                    MacAddress = d.MacAddress,
                    ChipId = d.ChipId,
                    FirmwareVersion = d.FirmwareVersion,
                    LastSeen = d.LastSeen,
                    ProvisionedAt = d.ProvisionedAt
                })
                .ToListAsync();

            _logger.LogInformation("User {UserId} retrieved {Count} pending devices", userId, pendingDevices.Count);
            return Ok(ApiResponse.Success(pendingDevices, "Pending devices retrieved"));
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "Unauthorized while getting pending devices");
            return ApiProblem(StatusCodes.Status401Unauthorized, "Unauthorized", "User not authenticated");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving pending devices");
            return ApiProblem(StatusCodes.Status500InternalServerError, "Internal Server Error", "Error retrieving pending devices");
        }
    }

    [HttpPost("self-register")]
    [AllowAnonymous]
    [EnableRateLimiting("device-onboarding")]
    public async Task<IActionResult> SelfRegister([FromBody] DeviceSelfRegisterRequestDto request)
    {
        try
        {
            var attemptKey = BuildSelfRegisterAttemptKey(request);
            if (TryGetCooldownResponse(attemptKey, out var cooldownResponse))
            {
                return cooldownResponse;
            }

            var providedKey = Request.Headers["X-Device-Key"].FirstOrDefault();
            var configuredKey = _provisioningOptions.SharedKey;

            if (string.IsNullOrWhiteSpace(configuredKey))
            {
                _logger.LogError("Provisioning:SharedKey is not configured");
                return ApiProblem(StatusCodes.Status500InternalServerError, "Internal Server Error", "Provisioning key is not configured");
            }

            if (string.IsNullOrWhiteSpace(providedKey) || !CryptographicOperations.FixedTimeEquals(System.Text.Encoding.UTF8.GetBytes(providedKey), System.Text.Encoding.UTF8.GetBytes(configuredKey)))
            {
                RegisterFailedAttempt(attemptKey);
                _logger.LogWarning("Invalid provisioning key for self-register request");
                return ApiProblem(StatusCodes.Status401Unauthorized, "Unauthorized", "Invalid provisioning key");
            }

            if (string.IsNullOrWhiteSpace(request.MacAddress) || !IsValidMacAddress(request.MacAddress))
            {
                RegisterFailedAttempt(attemptKey);
                return ApiProblem(StatusCodes.Status400BadRequest, "Bad Request", "Invalid MAC address format");
            }

            var normalizedMac = request.MacAddress.Trim().ToUpperInvariant();
            var device = await _context.Devices.FirstOrDefaultAsync(d => d.MacAddress == normalizedMac);

            if (device == null)
            {
                device = new Device
                {
                    DeviceName = string.IsNullOrWhiteSpace(request.DeviceName) ? "Pending Device" : request.DeviceName.Trim(),
                    MacAddress = normalizedMac,
                    ChipId = string.IsNullOrWhiteSpace(request.ChipId) ? null : request.ChipId.Trim(),
                    FirmwareVersion = string.IsNullOrWhiteSpace(request.FirmwareVersion) ? null : request.FirmwareVersion.Trim(),
                    Status = "Pending",
                    CreatedAt = DateTime.UtcNow,
                    LastSeen = DateTime.UtcNow,
                    ProvisionedAt = DateTime.UtcNow
                };

                _context.Devices.Add(device);
                await _context.SaveChangesAsync();
            }
            else
            {
                device.LastSeen = DateTime.UtcNow;
                device.ProvisionedAt ??= DateTime.UtcNow;
                if (!string.IsNullOrWhiteSpace(request.DeviceName))
                {
                    device.DeviceName = request.DeviceName.Trim();
                }
                if (!string.IsNullOrWhiteSpace(request.ChipId))
                {
                    device.ChipId = request.ChipId.Trim();
                }
                if (!string.IsNullOrWhiteSpace(request.FirmwareVersion))
                {
                    device.FirmwareVersion = request.FirmwareVersion.Trim();
                }
            }

            if (device.UserId != null)
            {
                device.Status = "Active";
                await _context.SaveChangesAsync();

                return Ok(ApiResponse.Success(new DeviceSelfRegisterResponseDto
                {
                    Success = true,
                    DeviceId = device.Id,
                    AlreadyClaimed = true,
                    Message = "Device already claimed"
                }, "Device already claimed"));
            }

            var claimCode = GenerateClaimCode();
            var claimCodeMinutes = _provisioningOptions.ClaimCodeMinutes;
            device.ClaimCode = claimCode;
            device.ClaimCodeExpiresAt = DateTime.UtcNow.AddMinutes(claimCodeMinutes);
            device.Status = "Pending";

            _context.Devices.Update(device);
            await _context.SaveChangesAsync();
            ResetFailedAttempts(attemptKey);

            return Ok(ApiResponse.Success(new DeviceSelfRegisterResponseDto
            {
                Success = true,
                DeviceId = device.Id,
                AlreadyClaimed = false,
                ClaimCode = device.ClaimCode,
                ClaimCodeExpiresAt = device.ClaimCodeExpiresAt,
                Message = "Device registered and waiting for claim"
            }, "Device registered and waiting for claim"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during device self-registration");
            return ApiProblem(StatusCodes.Status500InternalServerError, "Internal Server Error", "Error during self-registration");
        }
    }

    [HttpPost("claim")]
    [EnableRateLimiting("device-onboarding")]
    public async Task<IActionResult> ClaimDevice([FromBody] ClaimDeviceRequestDto request)
    {
        try
        {
            var userId = GetCurrentUserId();
            var attemptKey = BuildClaimAttemptKey(request);
            if (TryGetCooldownResponse(attemptKey, out var cooldownResponse))
            {
                return cooldownResponse;
            }

            if (string.IsNullOrWhiteSpace(request.ClaimCode))
            {
                RegisterFailedAttempt(attemptKey);
                return ApiProblem(StatusCodes.Status400BadRequest, "Bad Request", "Claim code is required");
            }

            var normalizedCode = request.ClaimCode.Trim().ToUpperInvariant();
            var device = await _context.Devices
                .FirstOrDefaultAsync(d => d.ClaimCode == normalizedCode && d.UserId == null);

            if (device == null)
            {
                RegisterFailedAttempt(attemptKey);
                return ApiProblem(StatusCodes.Status404NotFound, "Not Found", "Claim code is invalid or device already claimed");
            }

            if (!device.ClaimCodeExpiresAt.HasValue || device.ClaimCodeExpiresAt.Value < DateTime.UtcNow)
            {
                RegisterFailedAttempt(attemptKey);
                return ApiProblem(StatusCodes.Status400BadRequest, "Bad Request", "Claim code has expired. Restart device provisioning to get a new code");
            }

            if (request.CurrentCropId.HasValue)
            {
                var crop = await _context.Crops.FindAsync(request.CurrentCropId.Value);
                if (crop == null)
                {
                    return ApiProblem(StatusCodes.Status400BadRequest, "Bad Request", "Crop not found");
                }
                device.CurrentCropId = request.CurrentCropId;
                device.CropAssignedAt = DateTime.UtcNow;
            }

            if (request.GardenId.HasValue)
            {
                var garden = await _context.Gardens.FindAsync(request.GardenId.Value);
                if (garden == null)
                {
                    return ApiProblem(StatusCodes.Status400BadRequest, "Bad Request", "Garden not found");
                }
                device.GardenId = request.GardenId;
            }

            if (!string.IsNullOrWhiteSpace(request.Name))
            {
                device.DeviceName = request.Name.Trim();
            }

            device.UserId = userId;
            device.Status = "Active";
            device.ClaimCode = null;
            device.ClaimCodeExpiresAt = null;
            device.LastSeen = DateTime.UtcNow;

            _context.Devices.Update(device);
            await _context.SaveChangesAsync();
            ResetFailedAttempts(attemptKey);

            _logger.LogInformation("User {UserId} claimed device {DeviceId} ({MacAddress})", userId, device.Id, device.MacAddress);

            return Ok(ApiResponse.Success(new
            {
                detail = "Device claimed successfully",
                deviceId = device.Id,
                macAddress = device.MacAddress,
                name = device.DeviceName
            }, "Device claimed successfully"));
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "Unauthorized while claiming device");
            return ApiProblem(StatusCodes.Status401Unauthorized, "Unauthorized", "User not authenticated");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while claiming device");
            return ApiProblem(StatusCodes.Status500InternalServerError, "Internal Server Error", "Error claiming device");
        }
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetDeviceById(int id)
    {
        try
        {
            var userId = GetCurrentUserId();
            var userRole = GetCurrentUserRole();

            var device = await _context.Devices
                .Include(d => d.Crop)
                .Include(d => d.Garden)
                .FirstOrDefaultAsync(d => d.Id == id);

            if (device == null)
            {
                _logger.LogWarning("Device {DeviceId} not found", id);
                return ApiProblem(StatusCodes.Status404NotFound, "Not Found", "Device not found");
            }

            // Check authorization
            if (userRole != "Administrator" && device.UserId != userId)
            {
                _logger.LogWarning("User {UserId} attempted to access device {DeviceId} without permission", userId, id);
                return Forbid();
            }

            var deviceDto = new DeviceDto
            {
                Id = device.Id,
                Name = device.DeviceName ?? "Unknown Device",
                MacAddress = device.MacAddress,
                Status = device.Status,
                IsActive = device.IsActive,
                CurrentCropId = device.CurrentCropId,
                CropName = device.Crop?.Name,
                CropAssignedAt = device.CropAssignedAt,
                GardenId = device.GardenId,
                GardenName = device.Garden?.Name,
                CreatedAt = device.CreatedAt,
                LastSeen = device.LastSeen
            };

            return Ok(ApiResponse.Success(deviceDto, "Device retrieved"));
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "Unauthorized while getting device {DeviceId}", id);
            return ApiProblem(StatusCodes.Status401Unauthorized, "Unauthorized", "User not authenticated");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting device {DeviceId}", id);
            return ApiProblem(StatusCodes.Status500InternalServerError, "Internal Server Error", "Error retrieving device");
        }
    }

    [HttpPost]
    public async Task<IActionResult> CreateDevice([FromBody] CreateDeviceDto createDto)
    {
        try
        {
            if (!ModelState.IsValid)
            {
                return ValidationProblem(ModelState);
            }

            var userId = GetCurrentUserId();

            // Validate MAC address format
            if (!IsValidMacAddress(createDto.MacAddress))
            {
                return ApiProblem(StatusCodes.Status400BadRequest, "Bad Request", "Invalid MAC address format. Use AA:BB:CC:DD:EE:FF or AA-BB-CC-DD-EE-FF");
            }

            // Check if MAC address already exists
            var existingDevice = await _context.Devices
                .FirstOrDefaultAsync(d => d.MacAddress == createDto.MacAddress);

            if (existingDevice != null)
            {
                return ApiProblem(StatusCodes.Status400BadRequest, "Bad Request", "Device with this MAC address already exists");
            }

            // Validate crop if provided
            if (createDto.CurrentCropId.HasValue)
            {
                var crop = await _context.Crops.FindAsync(createDto.CurrentCropId);
                if (crop == null)
                {
                    return ApiProblem(StatusCodes.Status400BadRequest, "Bad Request", "Crop not found");
                }
            }

            if (createDto.GardenId.HasValue)
            {
                var garden = await _context.Gardens.FindAsync(createDto.GardenId);
                if (garden == null)
                {
                    return ApiProblem(StatusCodes.Status400BadRequest, "Bad Request", "Garden not found");
                }
            }

            var device = new Device
            {
                DeviceName = createDto.Name,
                MacAddress = createDto.MacAddress.ToUpper(),
                CurrentCropId = createDto.CurrentCropId,
                CropAssignedAt = createDto.CurrentCropId.HasValue ? DateTime.UtcNow : null,
                GardenId = createDto.GardenId,
                UserId = userId,
                Status = "Active",
                CreatedAt = DateTime.UtcNow,
                LastSeen = DateTime.UtcNow
            };

            _context.Devices.Add(device);
            await _context.SaveChangesAsync();

            _logger.LogInformation("User {UserId} created device {DeviceId} with MAC {MacAddress}",
                userId, device.Id, device.MacAddress);

            var deviceDto = new DeviceDto
            {
                Id = device.Id,
                Name = device.DeviceName ?? "Unknown Device",
                MacAddress = device.MacAddress,
                Status = device.Status,
                IsActive = device.IsActive,
                CurrentCropId = device.CurrentCropId,
                CropAssignedAt = device.CropAssignedAt,
                GardenId = device.GardenId,
                CreatedAt = device.CreatedAt,
                LastSeen = device.LastSeen
            };

            return CreatedAtAction(nameof(GetDeviceById), new { id = device.Id }, ApiResponse.Success(deviceDto, "Device created"));
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "Unauthorized while creating device");
            return ApiProblem(StatusCodes.Status401Unauthorized, "Unauthorized", "User not authenticated");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating device");
            return ApiProblem(StatusCodes.Status500InternalServerError, "Internal Server Error", "Error creating device");
        }
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateDevice(int id, [FromBody] UpdateDeviceDto updateDto)
    {
        try
        {
            if (!ModelState.IsValid)
            {
                return ValidationProblem(ModelState);
            }

            var userId = GetCurrentUserId();
            var userRole = GetCurrentUserRole();

            var device = await _context.Devices
                .Include(d => d.Crop)
                .Include(d => d.Garden)
                .FirstOrDefaultAsync(d => d.Id == id);

            if (device == null)
            {
                _logger.LogWarning("Device {DeviceId} not found for update", id);
                return ApiProblem(StatusCodes.Status404NotFound, "Not Found", "Device not found");
            }

            // Check authorization
            if (userRole != "Administrator" && device.UserId != userId)
            {
                _logger.LogWarning("User {UserId} attempted to update device {DeviceId} without permission", userId, id);
                return Forbid();
            }

            // Validate crop if provided
            if (updateDto.CurrentCropId.HasValue)
            {
                var crop = await _context.Crops.FindAsync(updateDto.CurrentCropId);
                if (crop == null)
                {
                    return ApiProblem(StatusCodes.Status400BadRequest, "Bad Request", "Crop not found");
                }

                if (device.CurrentCropId != updateDto.CurrentCropId)
                {
                    device.CropAssignedAt = DateTime.UtcNow;
                }
                device.CurrentCropId = updateDto.CurrentCropId;
            }
            else
            {
                device.CurrentCropId = null;
                device.CropAssignedAt = null;
            }

            if (updateDto.GardenId.HasValue)
            {
                var garden = await _context.Gardens.FindAsync(updateDto.GardenId);
                if (garden == null)
                {
                    return ApiProblem(StatusCodes.Status400BadRequest, "Bad Request", "Garden not found");
                }
                device.GardenId = updateDto.GardenId;
            }
            else
            {
                device.GardenId = null;
            }

            device.DeviceName = updateDto.Name ?? device.DeviceName;
            device.Status = updateDto.Status ?? device.Status;

            _context.Devices.Update(device);
            await _context.SaveChangesAsync();

            _logger.LogInformation("User {UserId} updated device {DeviceId}", userId, id);

            var deviceDto = new DeviceDto
            {
                Id = device.Id,
                Name = device.DeviceName ?? "Unknown Device",
                MacAddress = device.MacAddress,
                Status = device.Status,
                IsActive = device.IsActive,
                CurrentCropId = device.CurrentCropId,
                CropName = device.Crop?.Name,
                CropAssignedAt = device.CropAssignedAt,
                GardenId = device.GardenId,
                GardenName = device.Garden?.Name,
                CreatedAt = device.CreatedAt,
                LastSeen = device.LastSeen
            };

            return Ok(ApiResponse.Success(deviceDto, "Device updated"));
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "Unauthorized while updating device {DeviceId}", id);
            return ApiProblem(StatusCodes.Status401Unauthorized, "Unauthorized", "User not authenticated");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating device {DeviceId}", id);
            return ApiProblem(StatusCodes.Status500InternalServerError, "Internal Server Error", "Error updating device");
        }
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteDevice(int id)
    {
        try
        {
            var userId = GetCurrentUserId();
            var userRole = GetCurrentUserRole();

            var device = await _context.Devices
                .FirstOrDefaultAsync(d => d.Id == id);

            if (device == null)
            {
                _logger.LogWarning("Device {DeviceId} not found for deletion", id);
                return ApiProblem(StatusCodes.Status404NotFound, "Not Found", "Device not found");
            }

            // Check authorization
            if (userRole != "Administrator" && device.UserId != userId)
            {
                _logger.LogWarning("User {UserId} attempted to delete device {DeviceId} without permission", userId, id);
                return Forbid();
            }

            // Delete related logs with set-based operations to avoid loading large collections into memory.
            await _context.SensorLogs
                .Where(sl => sl.DeviceId == id)
                .ExecuteDeleteAsync();

            await _context.ActuatorLogs
                .Where(al => al.DeviceId == id)
                .ExecuteDeleteAsync();

            // Delete the device
            _context.Devices.Remove(device);
            await _context.SaveChangesAsync();

            _logger.LogInformation("User {UserId} deleted device {DeviceId}", userId, id);

            return Ok(ApiResponse.Success<object?>(null, "Device deleted successfully"));
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "Unauthorized while deleting device {DeviceId}", id);
            return ApiProblem(StatusCodes.Status401Unauthorized, "Unauthorized", "User not authenticated");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting device {DeviceId}", id);
            return ApiProblem(StatusCodes.Status500InternalServerError, "Internal Server Error", "Error deleting device");
        }
    }

    private IActionResult ApiProblem(int statusCode, string title, string detail)
    {
        return Problem(statusCode: statusCode, title: title, detail: detail);
    }

    private string BuildSelfRegisterAttemptKey(DeviceSelfRegisterRequestDto request)
    {
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var mac = request.MacAddress?.Trim().ToUpperInvariant() ?? "unknown";
        return $"self-register:{ip}:{mac}";
    }

    private string BuildClaimAttemptKey(ClaimDeviceRequestDto request)
    {
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var code = request.ClaimCode?.Trim().ToUpperInvariant() ?? "unknown";
        return $"claim:{ip}:{code}";
    }

    private bool TryGetCooldownResponse(string attemptKey, out IActionResult response)
    {
        response = default!;
        if (!TryGetCooldownRemainingSeconds(attemptKey, out var retryAfterSeconds))
        {
            return false;
        }

        Response.Headers["Retry-After"] = retryAfterSeconds.ToString();
        response = ApiProblem(StatusCodes.Status429TooManyRequests, "Too Many Requests", "Too many invalid onboarding attempts. Please retry later.");
        return true;
    }

    private static bool TryGetCooldownRemainingSeconds(string attemptKey, out int retryAfterSeconds)
    {
        retryAfterSeconds = 0;
        if (!FailedOnboardingAttempts.TryGetValue(attemptKey, out var state))
        {
            return false;
        }

        var now = DateTimeOffset.UtcNow;
        if (state.BlockedUntilUtc.HasValue && state.BlockedUntilUtc.Value > now)
        {
            retryAfterSeconds = (int)Math.Ceiling((state.BlockedUntilUtc.Value - now).TotalSeconds);
            return true;
        }

        if (state.BlockedUntilUtc.HasValue && state.BlockedUntilUtc.Value <= now)
        {
            FailedOnboardingAttempts.TryRemove(attemptKey, out _);
        }

        return false;
    }

    private static void RegisterFailedAttempt(string attemptKey)
    {
        var now = DateTimeOffset.UtcNow;

        FailedOnboardingAttempts.AddOrUpdate(
            attemptKey,
            _ => new OnboardingAttemptState(1, now, null),
            (_, current) =>
            {
                if (current.BlockedUntilUtc.HasValue && current.BlockedUntilUtc.Value > now)
                {
                    return current;
                }

                var windowStart = current.WindowStartUtc;
                var count = current.Count;

                if (now - windowStart > FailedAttemptWindow)
                {
                    windowStart = now;
                    count = 0;
                }

                count++;
                DateTimeOffset? blockedUntil = count >= FailedAttemptThreshold ? now.Add(FailedAttemptCooldown) : null;

                return new OnboardingAttemptState(count, windowStart, blockedUntil);
            });
    }

    private static void ResetFailedAttempts(string attemptKey)
    {
        FailedOnboardingAttempts.TryRemove(attemptKey, out _);
    }

    private sealed record OnboardingAttemptState(int Count, DateTimeOffset WindowStartUtc, DateTimeOffset? BlockedUntilUtc);

    // Helper method to validate MAC address format
    private bool IsValidMacAddress(string macAddress)
    {
        if (string.IsNullOrEmpty(macAddress))
            return false;

        // Accept both colon and dash separated formats: AA:BB:CC:DD:EE:FF or AA-BB-CC-DD-EE-FF
        var pattern = @"^([0-9A-Fa-f]{2}[:-]){5}([0-9A-Fa-f]{2})$";
        return System.Text.RegularExpressions.Regex.IsMatch(macAddress, pattern);
    }

    private static string GenerateClaimCode()
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        Span<byte> bytes = stackalloc byte[8];
        RandomNumberGenerator.Fill(bytes);
        var chars = new char[6];
        for (var i = 0; i < chars.Length; i++)
        {
            chars[i] = alphabet[bytes[i] % alphabet.Length];
        }
        return new string(chars);
    }




}
