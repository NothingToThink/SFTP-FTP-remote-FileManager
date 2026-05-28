using Core.Models;
using Core.Models.Credentials;

namespace Tests.Fakes;

public static class TestData
{
    public static SavedProfile CreateProfile(string name = "Test Server") =>
        SavedProfile.Create(name,
            new HostProfile("example.com", Protocol.Sftp, new AnonymousAuth()));

    public static SavedProfile CreatePasswordProfile(string name = "Auth Server") =>
        SavedProfile.Create(name,
            new HostProfile("example.com", Protocol.Sftp,
                new PasswordAuth("admin", "secret"), 2222));
}