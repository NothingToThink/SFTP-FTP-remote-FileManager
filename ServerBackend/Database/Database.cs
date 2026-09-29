using Microsoft.EntityFrameworkCore;
using ProfileServer.Models;
using System.ComponentModel.DataAnnotations;

[PrimaryKey(nameof(UserId), nameof(ProfileJson))]
public class UserProfile
{
  public Guid UserId { get; set; }
  public string ProfileJson { get; set; } = "";
}

public class UsernameAccount
{
  [Key]
  public string Username { get; set; } = string.Empty;
  public UserAccount Account { get; set; } = null!;
}

public class ServerDbContext : DbContext
{
  public ServerDbContext(DbContextOptions<ServerDbContext> options) : base(options) { }
  public DbSet<UsernameAccount> Accounts => Set<UsernameAccount>();
  public DbSet<UserProfile> Profiles => Set<UserProfile>();
  protected override void OnModelCreating(ModelBuilder mb)
  {
    mb.Entity<UsernameAccount>()
    .OwnsOne(a => a.Account, b =>
    {
        b.HasIndex(x => x.Id);
    });
  }
}
