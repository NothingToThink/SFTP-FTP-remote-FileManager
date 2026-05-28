using System;
using System.Collections.Generic;
using Backend.Controllers;
using Core.Models.Credentials;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Tests.Fakes;
using Xunit;

namespace Tests.Controllers;

public class ProfilesControllerTests
{
    private readonly FakeProfileManager _manager = new();
    private readonly ProfilesController _controller;

    public ProfilesControllerTests()
    {
        _controller = new ProfilesController(
            NullLogger<ProfilesController>.Instance,
            _manager);
    }
    

    [Fact]
    public void GetProfileIdList_Empty_ReturnsOkWithEmptyList()
    {
        var result = _controller.GetProfileIdList() as OkObjectResult;

        Assert.NotNull(result);
        var list = result.Value as List<Guid>;
        Assert.NotNull(list);
        Assert.Empty(list);
    }

    [Fact]
    public void GetProfileIdList_WithProfiles_ReturnsCorrectCount()
    {
        _manager.SaveProfile(TestData.CreateProfile("S1"));
        _manager.SaveProfile(TestData.CreateProfile("S2"));

        var result = _controller.GetProfileIdList() as OkObjectResult;
        var list = result?.Value as List<Guid>;

        Assert.Equal(2, list?.Count);
    }
    
    [Fact]
    public void GetProfile_Existing_ReturnsOkWithProfile()
    {
        var profile = TestData.CreateProfile();
        _manager.SaveProfile(profile);

        var result = _controller.GetProfile(profile.Id) as OkObjectResult;

        Assert.NotNull(result);
        var returned = result.Value as SavedProfile;
        Assert.Equal(profile.Id, returned?.Id);
    }

    [Fact]
    public void GetProfile_NonExistent_ReturnsBadRequest()
    {
        var result = _controller.GetProfile(Guid.NewGuid());

        Assert.IsType<BadRequestResult>(result);
    }
    
    [Fact]
    public void SaveProfile_Valid_ReturnsOkWithGuid()
    {
        var profile = TestData.CreateProfile();

        var result = _controller.SaveProfile(profile) as OkObjectResult;

        Assert.NotNull(result);
        Assert.IsType<Guid>(result.Value);
    }

    [Fact]
    public void SaveProfile_ThenGet_ReturnsSame()
    {
        var profile = TestData.CreateProfile("My Server");

        _controller.SaveProfile(profile);
        var result = _controller.GetProfile(profile.Id) as OkObjectResult;
        var returned = result?.Value as SavedProfile;

        Assert.Equal("My Server", returned?.Name);
    }
    
    [Fact]
    public void DeleteProfile_Existing_ReturnsOk()
    {
        var profile = TestData.CreateProfile();
        _manager.SaveProfile(profile);

        var result = _controller.DeleteProfile(profile.Id);

        Assert.IsType<OkResult>(result);
    }

    [Fact]
    public void DeleteProfile_NonExistent_ReturnsBadRequest()
    {
        var result = _controller.DeleteProfile(Guid.NewGuid());

        Assert.IsType<BadRequestResult>(result);
    }

    [Fact]
    public void DeleteProfile_ThenGet_ReturnsBadRequest()
    {
        var profile = TestData.CreateProfile();
        _manager.SaveProfile(profile);

        _controller.DeleteProfile(profile.Id);
        var result = _controller.GetProfile(profile.Id);

        Assert.IsType<BadRequestResult>(result);
    }
}