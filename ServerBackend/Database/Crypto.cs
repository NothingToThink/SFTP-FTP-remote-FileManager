using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;

public static class Crypto
{
  static readonly PasswordHasher<object> hasher = new();
  public static string Hash(string key) => hasher.HashPassword(null!, key);
  public static bool Verify(string key, string hash) =>
    hasher.VerifyHashedPassword(null!, hash, key) != PasswordVerificationResult.Failed;

  /// <summary>Папка данных сервера (app.db, app.key, jwt.key): SWINE_DATA_DIR, по умолчанию рабочая папка.</summary>
  public static string DataDir { get; } = ResolveDataDir();

  static string ResolveDataDir()
  {
    var dir = Environment.GetEnvironmentVariable("SWINE_DATA_DIR");
    if (string.IsNullOrWhiteSpace(dir)) return Directory.GetCurrentDirectory();
    Directory.CreateDirectory(dir);
    return dir;
  }

  static readonly byte[] Key = LoadKey();

  static byte[] LoadKey()
  {
    var path = Path.Combine(DataDir, "app.key");
    if (!File.Exists(path))
      File.WriteAllBytes(path, RandomNumberGenerator.GetBytes(32));
    return File.ReadAllBytes(path);
  }

  public static string Encrypt(string text)
  {
    var data = Encoding.UTF8.GetBytes(text);
    var iv = HMACSHA256.HashData(Key, data)[..16];
    using var aes = Aes.Create();
    aes.Key = Key;
    var cipher = aes.EncryptCbc(data, iv);
    return Convert.ToBase64String(iv.Concat(cipher).ToArray());
  }

  public static string Decrypt(string b64)
  {
    using var aes = Aes.Create();
    aes.Key = Key;
    var data = Convert.FromBase64String(b64);
    return Encoding.UTF8.GetString(aes.DecryptCbc(data[16..], data[..16]));
  }
}
