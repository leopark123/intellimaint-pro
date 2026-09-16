using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace IntelliMaint.Host.Api.Services;

// Deliberately accepted only by EdgeRead/EdgeWrite policies, never as a general API administrator.
public sealed class EdgeKeyHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "EdgeKey";
    private readonly IConfiguration _configuration;

    public EdgeKeyHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger,
        UrlEncoder encoder, IConfiguration configuration) : base(options, logger, encoder)
        => _configuration = configuration;

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var expected = _configuration["Edge:ApiKey"];
        if (!Request.Headers.TryGetValue("X-Edge-Key", out var supplied))
            return Task.FromResult(AuthenticateResult.NoResult());
        if (string.IsNullOrEmpty(expected) || expected.Length < 32 || supplied.Count != 1 ||
            !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected),
                Encoding.UTF8.GetBytes(supplied.ToString())))
            return Task.FromResult(AuthenticateResult.Fail("Invalid Edge credential."));

        var identity = new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "edge-service"),
            new Claim(ClaimTypes.Role, "Edge")
        }, SchemeName);
        return Task.FromResult(AuthenticateResult.Success(
            new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
    }
}
