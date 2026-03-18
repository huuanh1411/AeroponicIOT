using System.ComponentModel.DataAnnotations;

namespace AeroponicIOT.DTOs;

public class CreateAutomationRuleDto
{
    [Required]
    public int DeviceId { get; set; }

    [Required]
    [MaxLength(100)]
    public string RuleName { get; set; } = string.Empty;

    [Range(0, 2)]
    public int RuleType { get; set; }

    [Range(0, 3)]
    public int ActuatorType { get; set; }

    [Required]
    [MaxLength(10)]
    public string Action { get; set; } = "ON";

    [MaxLength(50)]
    public string? ConditionParameter { get; set; }

    public decimal? ConditionValue { get; set; }

    [MaxLength(10)]
    public string? ConditionOperator { get; set; }

    public TimeOnly? ScheduleTime { get; set; }

    [MaxLength(50)]
    public string? ScheduleDays { get; set; }

    [Range(0, 1440)]
    public int? DurationMinutes { get; set; }

    [Range(1, 10)]
    public int Priority { get; set; } = 1;

    public bool IsActive { get; set; } = true;
}

public class UpdateAutomationRuleDto
{
    [Required]
    [MaxLength(100)]
    public string RuleName { get; set; } = string.Empty;

    [Range(0, 2)]
    public int RuleType { get; set; }

    [Range(0, 3)]
    public int ActuatorType { get; set; }

    [Required]
    [MaxLength(10)]
    public string Action { get; set; } = "ON";

    [MaxLength(50)]
    public string? ConditionParameter { get; set; }

    public decimal? ConditionValue { get; set; }

    [MaxLength(10)]
    public string? ConditionOperator { get; set; }

    public TimeOnly? ScheduleTime { get; set; }

    [MaxLength(50)]
    public string? ScheduleDays { get; set; }

    [Range(0, 1440)]
    public int? DurationMinutes { get; set; }

    [Range(1, 10)]
    public int Priority { get; set; } = 1;
}