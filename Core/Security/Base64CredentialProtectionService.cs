using System;
using System.Text;

namespace Core.Security;

public class Base64CredentialProtectionService : ICredentialProtectionService
{
    private const string Prefix = "b64:";

    public string Encrypt(string password)
    {
        if (password == null)
        {
            throw new ArgumentNullException(nameof(password));
        }

        byte[] bytes = Encoding.UTF8.GetBytes(password);
        string encryptedPassword = Convert.ToBase64String(bytes);

        return Prefix + encryptedPassword;
    }
    public string Decrypt(string encryptedPassword)
    {
        if (encryptedPassword == null)
        {
            throw new ArgumentNullException(nameof(encryptedPassword));
        }

        if (!encryptedPassword.StartsWith(Prefix, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Wrong credential protection format.");
        }

        byte[] bytes = Convert.FromBase64String(encryptedPassword.Substring(Prefix.Length));
        return Encoding.UTF8.GetString(bytes);
    }
}
