namespace Core.Models ;
public class HostProfile
{
    public string Name { get; set; } = string.Empty;
    public int Port { get; set; }
    public string Protocol { get; set; } = string.Empty;
    public AuthData AuthData { get; set; } = new AuthData();
}
