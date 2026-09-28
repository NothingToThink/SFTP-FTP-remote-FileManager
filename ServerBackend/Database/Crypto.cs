using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;

public static class Crypto
{
  static readonly PasswordHasher<object> hasher = new();
  public static string Hash(string key) => hasher.HashPassword(null!, key);
  public static bool Verify(string key, string hash) =>
    hasher.VerifyHashedPassword(null!, hash, key) != PasswordVerificationResult.Failed;

  static readonly byte[] Key = LoadKey();

  static byte[] LoadKey()
  {
    const string path = "app.key";
    if (!File.Exists(path))
      File.WriteAllBytes(path, RandomNumberGenerator.GetBytes(32));
    return File.ReadAllBytes(path);
  }

  public static string Encrypt(string text)
  {
    using var aes = Aes.Create();
    aes.Key = Key;
    var cipher = aes.EncryptCbc(Encoding.UTF8.GetBytes(text), aes.IV);
    return Convert.ToBase64String(aes.IV.Concat(cipher).ToArray());
  }

  public static string Decrypt(string b64)
  {
    using var aes = Aes.Create();
    aes.Key = Key;
    var data = Convert.FromBase64String(b64);
    return Encoding.UTF8.GetString(aes.DecryptCbc(data[16..], data[..16]));
  }
}
