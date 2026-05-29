using System.ComponentModel.DataAnnotations;

namespace AeroponicIOT.DTOs;

public class UserAdminListItemDto
{
    public int Id { get; set; }
    public string? Username { get; set; }
    public string? Email { get; set; }
    public string? Role { get; set; }
    public DateTime? CreatedAt { get; set; }
    public DateTime? LastLogin { get; set; }
    public int DeviceCount { get; set; }
}

public class UpdateUserAdminRequest
{
    [EmailAddress]
    [StringLength(256)]
    public string? Email { get; set; }

    [Required]
    [StringLength(50)]
    public string? Role { get; set; }
}
