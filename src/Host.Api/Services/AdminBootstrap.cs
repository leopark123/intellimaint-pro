using IntelliMaint.Core.Abstractions;
using IntelliMaint.Core.Contracts;
using IntelliMaint.Host.Api.Validators;

namespace IntelliMaint.Host.Api.Services;

public static class AdminBootstrap
{
    public static async Task InitializeAsync(IServiceProvider services, IConfiguration configuration,
        CancellationToken ct = default)
    {
        var users = services.GetRequiredService<IUserRepository>();
        if ((await users.ListAsync(ct)).Count > 0) return;

        var username = configuration["ADMIN_USERNAME"];
        var password = configuration["ADMIN_PASSWORD"];
        if (!InputValidator.ValidateUsername(username).IsValid ||
            string.IsNullOrWhiteSpace(password) || password.Length < 16 ||
            System.Text.Encoding.UTF8.GetByteCount(password) > 72)
            throw new InvalidOperationException(
                "Empty database: set ADMIN_USERNAME (3-50 letters, digits or underscores) and ADMIN_PASSWORD (16+ characters, at most 72 UTF-8 bytes). No default account is created.");

        var created = await users.CreateAsync(username!, password, UserRoles.Admin, "Administrator", ct);
        if (created is null)
            throw new InvalidOperationException("First administrator could not be created. Check the database and username.");
        services.GetRequiredService<ILoggerFactory>().CreateLogger("AdminBootstrap")
            .LogInformation("First administrator initialized. Remove ADMIN_PASSWORD from the deployment environment after first startup.");
    }
}
