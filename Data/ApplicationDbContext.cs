using AeroponicIOT.Models;
using Microsoft.EntityFrameworkCore;

namespace AeroponicIOT.Data;

public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    public DbSet<Device> Devices { get; set; }
    public DbSet<Crop> Crops { get; set; }
    public DbSet<CropStage> CropStages { get; set; }
    public DbSet<SensorLog> SensorLogs { get; set; }
    public DbSet<ActuatorLog> ActuatorLogs { get; set; }
    public DbSet<User> Users { get; set; }
    public DbSet<Alert> Alerts { get; set; }
    public DbSet<Notification> Notifications { get; set; }
    public DbSet<AutomationRule> AutomationRules { get; set; }
    public DbSet<Garden> Gardens { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Configure Device properties
        modelBuilder.Entity<Device>()
            .Property(d => d.UserId)
            .HasColumnName("user_id");

        // Configure relationships
        modelBuilder.Entity<Device>()
            .HasOne(d => d.Crop)
            .WithMany(c => c.Devices)
            .HasForeignKey(d => d.CurrentCropId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<Device>()
            .HasOne(d => d.Garden)
            .WithMany(g => g.Devices)
            .HasForeignKey(d => d.GardenId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<Device>()
            .HasOne(d => d.User)
            .WithMany(u => u.Devices)
            .HasForeignKey(d => d.UserId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<CropStage>()
            .HasOne(cs => cs.Crop)
            .WithMany(c => c.CropStages)
            .HasForeignKey(cs => cs.CropId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<SensorLog>()
            .HasOne(sl => sl.Device)
            .WithMany(d => d.SensorLogs)
            .HasForeignKey(sl => sl.DeviceId)
            .OnDelete(DeleteBehavior.Cascade);

        // Indexes for faster queries on logs
        modelBuilder.Entity<SensorLog>()
            .HasIndex(sl => new { sl.DeviceId, sl.Timestamp });

        modelBuilder.Entity<ActuatorLog>()
            .HasOne(al => al.Device)
            .WithMany(d => d.ActuatorLogs)
            .HasForeignKey(al => al.DeviceId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<ActuatorLog>()
            .HasIndex(al => new { al.DeviceId, al.Timestamp });

        modelBuilder.Entity<Alert>()
            .HasOne(a => a.Device)
            .WithMany()
            .HasForeignKey(a => a.DeviceId)
            .OnDelete(DeleteBehavior.SetNull);

        // Note: Removed seed data since we're connecting to existing database
    }
}