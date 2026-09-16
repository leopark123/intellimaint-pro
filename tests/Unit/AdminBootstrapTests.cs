using IntelliMaint.Core.Abstractions;
using IntelliMaint.Core.Contracts;
using IntelliMaint.Host.Api.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace IntelliMaint.Tests.Unit;

public sealed class AdminBootstrapTests
{
    [Theory]
    [InlineData(null, null)]
    [InlineData("new_admin", "too-short")]
    [InlineData("invalid-user", "long-enough-but-test-only-password")]
    [InlineData("new_admin", "超超超超超超超超超超超超超超超超超超超超超超超超超")]
    public async Task InvalidBootstrapNeverCreatesAnAccount(string? username, string? password)
    {
        var users = new Mock<IUserRepository>();
        users.Setup(x => x.ListAsync(default)).ReturnsAsync(Array.Empty<UserDto>());
        using var services = Services(users.Object);
        await Assert.ThrowsAsync<InvalidOperationException>(() => AdminBootstrap.InitializeAsync(services, Config(username, password)));
        users.Verify(x => x.CreateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), default), Times.Never);
    }

    [Fact]
    public async Task ExistingAccountsArePreservedWithoutBootstrapCredentials()
    {
        var users = new Mock<IUserRepository>();
        users.Setup(x => x.ListAsync(default)).ReturnsAsync(new[] { new UserDto { UserId = "existing", Username = "existing_admin", Role = UserRoles.Admin } });
        using var services = Services(users.Object);
        await AdminBootstrap.InitializeAsync(services, Config(null, null));
        users.Verify(x => x.CreateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), default), Times.Never);
        users.Verify(x => x.ListAsync(default), Times.Once);
        users.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task FailedAccountCreationStopsStartup()
    {
        var users = new Mock<IUserRepository>();
        users.Setup(x => x.ListAsync(default)).ReturnsAsync(Array.Empty<UserDto>());
        using var services = Services(users.Object);
        await Assert.ThrowsAsync<InvalidOperationException>(() => AdminBootstrap.InitializeAsync(services, Config("new_admin", Guid.NewGuid().ToString("N"))));
    }

    private static IConfiguration Config(string? username, string? password) => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { ["ADMIN_USERNAME"] = username, ["ADMIN_PASSWORD"] = password }).Build();
    private static ServiceProvider Services(IUserRepository users) => new ServiceCollection().AddLogging()
        .AddSingleton(users).BuildServiceProvider();
}
