namespace Core.Ssh.HostKey;

/// <summary>
/// Identity of a server's host key, as presented during the SSH handshake.
/// </summary>
/// <param name="Algorithm">Host key algorithm name, e.g. <c>ssh-ed25519</c>.</param>
/// <param name="Sha256Fingerprint">Base64 SHA-256 fingerprint, the same value OpenSSH prints as <c>SHA256:...</c>.</param>
public record HostKeyInfo(string Algorithm, string Sha256Fingerprint)
{
    /// <summary>Fingerprint in the format OpenSSH shows to the user.</summary>
    public string Display => $"SHA256:{Sha256Fingerprint}";
}
