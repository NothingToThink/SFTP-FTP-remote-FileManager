namespace Core.Models.Credentials ;

public record HostProfile(
    string Host,
    Protocol Protocol,
    AuthData Auth,
    int? Port = null)
{
    public int EffectivePort => Port ?? DefaultPortFor(Protocol);

    private static int DefaultPortFor(Protocol protocol) => protocol switch
    {
        Protocol.Ftp => 21,
        Protocol.Sftp => 22,
        Protocol.Local => 0, 
        _ => throw new ArgumentOutOfRangeException(nameof(Protocol))
    };
}
