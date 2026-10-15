using FileManagerClient.Api;
using FileManagerClient.Services;
using Xunit;

namespace Tests;

public class ErrorPresenterTests
{
    [Fact]
    public void Permission_Denied_On_A_File_Is_Access_Denied_Not_A_Wrong_Password()
    {
        var error = ErrorPresenter.Present(new ApiException(403, "Permission denied"),
            new ErrorContext("prod-01", IsConnecting: false, Path: "/etc/shadow"));

        Assert.Equal(UserErrorKind.AccessDenied, error.Kind);
        Assert.Equal("Нет доступа", error.Title);
    }

    [Fact]
    public void Permission_Denied_While_Connecting_Is_A_Wrong_Password()
    {
        var error = ErrorPresenter.Present(new ApiException(500, "Permission denied (password)."),
            new ErrorContext("prod-01", IsConnecting: true));

        Assert.Equal("Хрю, не пускают", error.Title);
    }

    [Fact]
    public void Status_401_Is_A_Wrong_Password_Even_Outside_Connecting()
    {
        var error = ErrorPresenter.Present(new ApiException(401, "Unauthorized"), new ErrorContext("prod-01"));

        Assert.Equal("Хрю, не пускают", error.Title);
    }
}
