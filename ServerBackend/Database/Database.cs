using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
namespace Core.Models.Credentials;


public class UserProfile
{
  public Guid UserId { get; set; }
  [Key]
  public Guid Id { get; set; }
  public string Name { get; set; } = string.Empty;
  public string JsonHostProfile { get; set; } = string.Empty;

  private UserProfile() { }

  public UserProfile(Guid userId, SavedProfile savedProfile)
  {
    UserId = userId;
    Id = savedProfile.Id;
    Name = savedProfile.Name;
    JsonHostProfile = Crypto.Encrypt(JsonSerializer.Serialize(savedProfile.HostProfile));
  }
}

public class UsernameAccount
{
  [Key]
  public string Username { get; set; } = string.Empty;
  public Guid Id { get; set; } = Guid.NewGuid();
  public string HashedPassword { get; set; }

  public UsernameAccount(string username, string hashedPassword)
  {
    Username = username;
    HashedPassword = hashedPassword;
  }
}

public class ServerDbContext : DbContext
{
  public ServerDbContext(DbContextOptions<ServerDbContext> options) : base(options) { }
  public DbSet<UsernameAccount> Accounts => Set<UsernameAccount>();
  public DbSet<UserProfile> Profiles => Set<UserProfile>();
  protected override void OnModelCreating(ModelBuilder mb)
  {
    mb.Entity<UserProfile>().HasIndex(p => p.UserId);

    mb.Entity<UserProfile>()
    .HasOne<UsernameAccount>()
    .WithMany()
    .HasForeignKey(p => p.UserId)
    .HasPrincipalKey(a => a.Id)
    .OnDelete(DeleteBehavior.Cascade);

    mb.Entity<UsernameAccount>().HasIndex(a => a.Id).IsUnique();
  }
}
