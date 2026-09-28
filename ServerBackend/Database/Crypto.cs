using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;

public static class Crypto
{
  static readonly PasswordHasher<object> hasher = new();
  public static string Hash(string key) => hasher.HashPassword(null!, key);
  public static bool Verify(string key, string hash) =>
    hasher.VerifyHashedPassword(null!, hash, key) != PasswordVerificationResult.Failed;

  static Aes CreateAes(string key)
  {
    var aes = Aes.Create();
    aes.Key = SHA256.HashData(Encoding.UTF8.GetBytes(key));
    return aes;
  }

  public static string Encrypt(string text, string key)
  {
    using var aes = CreateAes(key);
    var cipher = aes.EncryptCbc(Encoding.UTF8.GetBytes(text), aes.IV);
    return Convert.ToBase64String(aes.IV.Concat(cipher).ToArray());
  }

  public static string Decrypt(string b64, string key)
  {
    using var aes = CreateAes(key);
    var data = Convert.FromBase64String(b64);
    return Encoding.UTF8.GetString(aes.DecryptCbc(data[16..], data[..16]));
  }
}
