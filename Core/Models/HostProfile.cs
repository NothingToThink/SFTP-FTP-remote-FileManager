namespace Core.Models ;
public class HostProfile
{
    public string Name { get; set; } = string.Empty;
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; }
    public ProtocolName Protocol { get; set; } = ProtocolName.local;
    public AuthData AuthData { get; set; } = new AuthData();
}
