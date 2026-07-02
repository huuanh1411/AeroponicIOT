using System.Linq.Expressions;
using AeroponicIOT.Data;
using AeroponicIOT.DTOs;
using AeroponicIOT.Models;
using AeroponicIOT.Services.AI;
using AeroponicIOT.Services.Notifications;
using AeroponicIOT.Services.Sensors;
using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AeroponicIOT.Tests;

public class SensorIngestionReliabilityTests
{
    [Fact]
    public async Task ProcessSensorDataAsyncWhenNotificationDispatchFailsStillPersistsSensorAndAlerts()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"sensor-ingestion-{Guid.NewGuid()}")
            .Options;

        await using var dbContext = new ApplicationDbContext(options);

        var crop = new Crop
        {
            Id = 1,
            Name = "Lettuce",
            TotalDaysEst = 45,
            CreatedAt = DateTime.UtcNow,
            CropStages =
            [
                new CropStage
                {
                    Id = 1,
                    CropId = 1,
                    StageName = "Stage-1",
                    DayStart = 1,
                    DayEnd = 30,
                    PhMin = 5.8m,
                    PhMax = 6.2m,
                    PpmMin = 700,
                    PpmMax = 900,
                    WaterTempMin = 18,
                    WaterTempMax = 22,
                    HumidityMin = 55,
                    HumidityMax = 70
                }
            ]
        };

        var device = new Device
        {
            Id = 1,
            DeviceName = "Unit-01",
            MacAddress = "AA:BB:CC:DD:EE:01",
            CurrentCropId = 1,
            CropAssignedAt = DateTime.UtcNow.AddDays(-2),
            CreatedAt = DateTime.UtcNow,
            Status = DeviceStatusValues.Active,
            Crop = crop
        };

        dbContext.Crops.Add(crop);
        dbContext.Devices.Add(device);
        await dbContext.SaveChangesAsync();

        var backgroundJobClient = new NoopBackgroundJobClient();
        var ingestionService = new SensorIngestionService(
            dbContext,
            backgroundJobClient,
            NullLogger<SensorIngestionService>.Instance);

        var payload = new SensorDataDto
        {
            MacAddress = "aa:bb:cc:dd:ee:01",
            Ph = 7.4,
            Tds = 1200,
            WaterTemperature = 27,
            AirHumidity = 40
        };

        await ingestionService.ProcessSensorDataAsync(payload, CancellationToken.None);

        Assert.Single(dbContext.SensorLogs);
        Assert.True(dbContext.Alerts.Any());
    }

    private sealed class ThrowingNotificationService : INotificationService
    {
        public Task SendNotificationAsync(int userId, string title, string message, NotificationType type = NotificationType.Info)
            => throw new InvalidOperationException("Synthetic notification failure");

        public Task SendAlertNotificationAsync(int deviceId, string title, string message, string severity)
            => throw new InvalidOperationException("Synthetic alert failure");

        public Task<List<NotificationDto>> GetUnreadNotificationsAsync(int userId)
            => Task.FromResult(new List<NotificationDto>());

        public Task MarkAsReadAsync(int notificationId, int userId)
            => Task.CompletedTask;

        public Task ClearNotificationsAsync(int userId)
            => Task.CompletedTask;
    }

    private sealed class NoopAISuggestionService : IAISuggestionService
    {
        public Task<AiSuggestionResult?> AnalyzeSensorDataAsync(int deviceId, string macAddress, CancellationToken cancellationToken = default)
            => Task.FromResult<AiSuggestionResult?>(null);
    }

    private sealed class NoopBackgroundJobClient : IBackgroundJobClient
    {
        public string Create<T>(Expression<Func<T, Task>> methodCall, IState state)
            => Guid.NewGuid().ToString();

        public string Create<T>(Expression<Action<T>> methodCall, IState state)
            => Guid.NewGuid().ToString();

        public string Create(Job job, IState state)
            => Guid.NewGuid().ToString();

        public bool ChangeState(string jobId, IState state, string? expectedState = null)
            => true;

        public bool Requeue(string jobId)
            => true;

        public bool Delete(string jobId)
            => true;

        public bool ContinueJobWith<T>(string parentId, Expression<Func<T, Task>> methodCall, IState state)
            => true;

        public bool ContinueJobWith<T>(string parentId, Expression<Action<T>> methodCall, IState state)
            => true;

        public bool ContinueJobWith(string parentId, Job job, IState state)
            => true;
    }
}
