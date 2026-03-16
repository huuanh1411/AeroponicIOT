using AeroponicIOT.Data;
using AeroponicIOT.Services.Automation;
using AeroponicIOT.Services.Mqtt;
using AeroponicIOT.Services.Notifications;
using AeroponicIOT.Services.Sensors;
using AeroponicIOT.Services.Maintenance;
using AeroponicIOT.Middleware;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddOpenApi();

// Add CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

// Add JWT Authentication
var jwtSettings = builder.Configuration.GetSection("JwtSettings");
var secretKey = jwtSettings["SecretKey"];
if (string.IsNullOrWhiteSpace(secretKey))
{
    throw new InvalidOperationException("JWT SecretKey is not configured. Set JwtSettings:SecretKey via environment or configuration.");
}

var key = Encoding.ASCII.GetBytes(secretKey);

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(key),
        ValidateIssuer = true,
        ValidIssuer = jwtSettings["Issuer"],
        ValidateAudience = true,
        ValidAudience = jwtSettings["Audience"],
        ValidateLifetime = true,
        ClockSkew = TimeSpan.Zero
    };
});

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AdminOnly", policy => policy.RequireRole("Administrator"));
    options.AddPolicy("FarmerOrAdmin", policy => policy.RequireRole("Farmer", "Administrator"));
});

// Add DbContext
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// Add MQTT Service
builder.Services.AddSingleton<IMqttService, MqttService>();

// Add Notification Services
builder.Services.AddScoped<IEmailService, EmailService>();
builder.Services.AddScoped<INotificationService, NotificationService>();

// Sensor ingestion (shared by HTTP and MQTT)
builder.Services.AddScoped<ISensorIngestionService, SensorIngestionService>();

// Automation background service
builder.Services.AddHostedService<AutomationBackgroundService>();
builder.Services.AddHostedService<LogRetentionBackgroundService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseCors("AllowAll");
app.UseAuthentication();
app.UseAuthorization();

// Serve static files
app.UseStaticFiles();

// Health endpoint (DB + MQTT)
app.MapGet("/health", async (ApplicationDbContext db, IMqttService mqtt, CancellationToken ct) =>
{
    try
    {
        var canConnect = await db.Database.CanConnectAsync(ct);
        if (!canConnect)
        {
            return Results.Json(new
            {
                status = "Unhealthy",
                db = "Unavailable",
                mqtt = mqtt.IsRunning ? "Running" : "Stopped",
                timestamp = DateTime.UtcNow
            }, statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }
    catch (Exception ex)
    {
        return Results.Json(new
        {
            status = "Unhealthy",
            db = "Error",
            mqtt = mqtt.IsRunning ? "Running" : "Stopped",
            error = ex.Message,
            timestamp = DateTime.UtcNow
        }, statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    if (!mqtt.IsRunning)
    {
        return Results.Json(new
        {
            status = "Unhealthy",
            db = "Connected",
            mqtt = "Stopped",
            timestamp = DateTime.UtcNow
        }, statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    return Results.Ok(new
    {
        status = "Healthy",
        db = "Connected",
        mqtt = "Running",
        timestamp = DateTime.UtcNow
    });
});

// Default route to dashboard
app.MapGet("/", async context =>
{
    context.Response.ContentType = "text/html";
    await context.Response.SendFileAsync("wwwroot/index.html");
});

app.MapControllers();

// Ensure database connection (don't create if it doesn't exist)
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    // Apply migrations on startup so the container can initialize schema automatically.
    try
    {
        dbContext.Database.Migrate();
    }
    catch (Exception ex)
    {
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
        logger.LogError(ex, "An error occurred while migrating or initializing the database.");
        // Let the app continue to run; in container environments the DB may come up after the app.
    }
}

// Start MQTT Broker
try
{
    var mqttService = app.Services.GetRequiredService<IMqttService>();
    await mqttService.StartAsync();
}
catch (Exception ex)
{
    var logger = app.Services.GetRequiredService<ILogger<Program>>();
    logger.LogError(ex, "Failed to start MQTT broker");
}

app.Run();
