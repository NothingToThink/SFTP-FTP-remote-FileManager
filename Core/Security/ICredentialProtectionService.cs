namespace Core.Security;
public interface ICredentialProtectionService
{
    string Encrypt(string password);
    string Decrypt(string encryptedPassword);
}