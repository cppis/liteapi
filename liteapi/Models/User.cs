using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace liteapi.Models;

/// <summary>
/// User entity for EF Core
/// </summary>
[Table("users")]
public class User
{
    [Key]
    [Column("user_id")]
    public ulong UserId { get; set; }

    [Required]
    [MaxLength(50)]
    [Column("username")]
    public string Username { get; set; } = string.Empty;

    [MaxLength(100)]
    [Column("email")]
    public string? Email { get; set; }

    [Column("level")]
    public int Level { get; set; } = 1;

    [Column("experience")]
    public long Experience { get; set; } = 0;

    [Column("gold")]
    public long Gold { get; set; } = 0;

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
