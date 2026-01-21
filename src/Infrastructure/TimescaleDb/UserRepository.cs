using Dapper;
using IntelliMaint.Core.Abstractions;
using IntelliMaint.Core.Contracts;
using Microsoft.Extensions.Logging;

namespace IntelliMaint.Infrastructure.TimescaleDb;

/// <summary>
/// TimescaleDB User repository implementation
/// </summary>
public sealed class UserRepository : IUserRepository
{
    private readonly INpgsqlConnectionFactory _factory;
    private readonly ILogger<UserRepository> _logger;

    // Account lockout configuration
    private const int MaxFailedAttempts = 5;
    private const int LockoutMinutes = 15;

    public UserRepository(INpgsqlConnectionFactory factory, ILogger<UserRepository> logger)
    {
        _factory = factory;
        _logger = logger;
    }

    public async Task<UserDto?> GetByUsernameAsync(string username, CancellationToken ct)
    {
        const string sql = @"
            SELECT user_id, username, display_name, role, enabled, created_utc, last_login_utc, must_change_password
            FROM ""user"" WHERE LOWER(username) = LOWER(@Username)";

        using var conn = _factory.CreateConnection();
        var row = await conn.QueryFirstOrDefaultAsync<UserRow>(
            new CommandDefinition(sql, new { Username = username }, cancellationToken: ct));
        return row is null ? null : MapToDto(row);
    }

    public async Task<UserDto?> GetByIdAsync(string userId, CancellationToken ct)
    {
        const string sql = @"
            SELECT user_id, username, display_name, role, enabled, created_utc, last_login_utc, must_change_password
            FROM ""user"" WHERE user_id = @UserId";

        using var conn = _factory.CreateConnection();
        var row = await conn.QueryFirstOrDefaultAsync<UserRow>(
            new CommandDefinition(sql, new { UserId = userId }, cancellationToken: ct));
        return row is null ? null : MapToDto(row);
    }

    public async Task<UserDto?> ValidateCredentialsAsync(string username, string password, CancellationToken ct)
    {
        const string sql = @"
            SELECT user_id, username, display_name, role, enabled, created_utc, last_login_utc,
                   password_hash, failed_login_count, lockout_until_utc, must_change_password
            FROM ""user"" WHERE LOWER(username) = LOWER(@Username) AND enabled = true";

        using var conn = _factory.CreateConnection();
        var row = await conn.QueryFirstOrDefaultAsync<UserRowWithPassword>(
            new CommandDefinition(sql, new { Username = username }, cancellationToken: ct));

        if (row is null)
            return null;

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        // Check if account is locked
        if (row.lockout_until_utc.HasValue && row.lockout_until_utc.Value > now)
            return null;

        // Verify password using BCrypt
        bool passwordValid;
        try
        {
            passwordValid = BCrypt.Net.BCrypt.Verify(password, row.password_hash);
        }
        catch
        {
            // Try legacy SHA256 format for migration
            passwordValid = VerifyLegacyPassword(password, row.password_hash);
            if (passwordValid)
            {
                await UpgradePasswordHashAsync(row.user_id, password, ct);
            }
        }

        if (!passwordValid)
        {
            await IncrementFailedLoginAsync(row.user_id, ct);
            return null;
        }

        // Reset failed login count on success
        await ResetFailedLoginAsync(row.user_id, ct);

        return new UserDto
        {
            UserId = row.user_id,
            Username = row.username,
            DisplayName = row.display_name,
            Role = row.role,
            Enabled = row.enabled,
            CreatedUtc = row.created_utc,
            LastLoginUtc = row.last_login_utc,
            MustChangePassword = row.must_change_password
        };
    }

    public async Task<UserDto?> CreateAsync(string username, string password, string role, string? displayName, CancellationToken ct)
    {
        var userId = Guid.NewGuid().ToString("N")[..16];
        var passwordHash = HashPassword(password);
        var nowUtc = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        const string sql = @"
            INSERT INTO ""user"" (user_id, username, password_hash, display_name, role, enabled, created_utc, must_change_password)
            VALUES (@UserId, @Username, @PasswordHash, @DisplayName, @Role, true, @CreatedUtc, true)";

        using var conn = _factory.CreateConnection();
        await conn.ExecuteAsync(new CommandDefinition(sql, new
        {
            UserId = userId,
            Username = username,
            PasswordHash = passwordHash,
            DisplayName = displayName,
            Role = role,
            CreatedUtc = nowUtc
        }, cancellationToken: ct));

        _logger.LogInformation("Created user {UserId} with username {Username}", userId, username);

        return new UserDto
        {
            UserId = userId,
            Username = username,
            DisplayName = displayName,
            Role = role,
            Enabled = true,
            CreatedUtc = nowUtc
        };
    }

    public async Task<UserDto?> UpdateAsync(string userId, string? displayName, string? role, bool? enabled, CancellationToken ct)
    {
        var sets = new List<string>();
        var p = new DynamicParameters();
        p.Add("UserId", userId);

        if (displayName != null)
        {
            sets.Add("display_name = @DisplayName");
            p.Add("DisplayName", displayName);
        }
        if (role != null)
        {
            sets.Add("role = @Role");
            p.Add("Role", role);
        }
        if (enabled.HasValue)
        {
            sets.Add("enabled = @Enabled");
            p.Add("Enabled", enabled.Value);
        }

        if (sets.Count == 0)
            return await GetByIdAsync(userId, ct);

        var sql = $@"UPDATE ""user"" SET {string.Join(", ", sets)} WHERE user_id = @UserId";

        using var conn = _factory.CreateConnection();
        await conn.ExecuteAsync(new CommandDefinition(sql, p, cancellationToken: ct));

        return await GetByIdAsync(userId, ct);
    }

    public async Task<bool> UpdatePasswordAsync(string userId, string currentPassword, string newPassword, CancellationToken ct)
    {
        const string selectSql = @"SELECT password_hash FROM ""user"" WHERE user_id = @UserId AND enabled = true";

        using var conn = _factory.CreateConnection();
        var storedHash = await conn.ExecuteScalarAsync<string?>(
            new CommandDefinition(selectSql, new { UserId = userId }, cancellationToken: ct));

        if (storedHash is null)
            return false;

        bool passwordValid;
        try
        {
            passwordValid = BCrypt.Net.BCrypt.Verify(currentPassword, storedHash);
        }
        catch
        {
            passwordValid = VerifyLegacyPassword(currentPassword, storedHash);
        }

        if (!passwordValid)
            return false;

        var newHash = HashPassword(newPassword);
        // Clear must_change_password flag when user changes their own password
        const string updateSql = @"UPDATE ""user"" SET password_hash = @PasswordHash, must_change_password = false WHERE user_id = @UserId";
        var affected = await conn.ExecuteAsync(
            new CommandDefinition(updateSql, new { PasswordHash = newHash, UserId = userId }, cancellationToken: ct));

        return affected > 0;
    }

    public async Task<bool> ResetPasswordAsync(string userId, string newPassword, CancellationToken ct)
    {
        var newHash = HashPassword(newPassword);
        // Set must_change_password flag when admin resets password
        const string sql = @"UPDATE ""user"" SET password_hash = @PasswordHash, must_change_password = true WHERE user_id = @UserId";

        using var conn = _factory.CreateConnection();
        var affected = await conn.ExecuteAsync(
            new CommandDefinition(sql, new { PasswordHash = newHash, UserId = userId }, cancellationToken: ct));

        return affected > 0;
    }

    public async Task<bool> DisableAsync(string userId, CancellationToken ct)
    {
        const string sql = @"
            UPDATE ""user"" SET enabled = false, refresh_token = NULL, refresh_token_expires_utc = NULL
            WHERE user_id = @UserId";

        using var conn = _factory.CreateConnection();
        var affected = await conn.ExecuteAsync(
            new CommandDefinition(sql, new { UserId = userId }, cancellationToken: ct));

        return affected > 0;
    }

    public async Task UpdateLastLoginAsync(string userId, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        const string sql = @"UPDATE ""user"" SET last_login_utc = @Now WHERE user_id = @UserId";

        using var conn = _factory.CreateConnection();
        await conn.ExecuteAsync(
            new CommandDefinition(sql, new { Now = now, UserId = userId }, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<UserDto>> ListAsync(CancellationToken ct)
    {
        const string sql = @"
            SELECT user_id, username, display_name, role, enabled, created_utc, last_login_utc, must_change_password
            FROM ""user"" ORDER BY created_utc DESC";

        using var conn = _factory.CreateConnection();
        var rows = await conn.QueryAsync<UserRow>(new CommandDefinition(sql, cancellationToken: ct));
        return rows.Select(MapToDto).ToList();
    }

    public async Task SaveRefreshTokenAsync(string userId, string refreshToken, long expiresUtc, CancellationToken ct)
    {
        const string sql = @"
            UPDATE ""user"" SET refresh_token = @RefreshToken, refresh_token_expires_utc = @ExpiresUtc
            WHERE user_id = @UserId";

        using var conn = _factory.CreateConnection();
        await conn.ExecuteAsync(new CommandDefinition(sql, new
        {
            UserId = userId,
            RefreshToken = refreshToken,
            ExpiresUtc = expiresUtc
        }, cancellationToken: ct));
    }

    public async Task<UserDto?> GetByRefreshTokenAsync(string refreshToken, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        const string sql = @"
            SELECT user_id, username, display_name, role, enabled, created_utc, last_login_utc,
                   refresh_token_expires_utc, must_change_password
            FROM ""user""
            WHERE refresh_token = @RefreshToken AND enabled = true";

        using var conn = _factory.CreateConnection();
        var row = await conn.QueryFirstOrDefaultAsync<UserRowWithExpiry>(
            new CommandDefinition(sql, new { RefreshToken = refreshToken }, cancellationToken: ct));

        if (row is null)
            return null;

        // Check if token is expired
        if (row.refresh_token_expires_utc < now)
            return null;

        return new UserDto
        {
            UserId = row.user_id,
            Username = row.username,
            DisplayName = row.display_name,
            Role = row.role,
            Enabled = row.enabled,
            CreatedUtc = row.created_utc,
            LastLoginUtc = row.last_login_utc,
            MustChangePassword = row.must_change_password
        };
    }

    public async Task ClearRefreshTokenAsync(string userId, CancellationToken ct)
    {
        const string sql = @"
            UPDATE ""user"" SET refresh_token = NULL, refresh_token_expires_utc = NULL
            WHERE user_id = @UserId";

        using var conn = _factory.CreateConnection();
        await conn.ExecuteAsync(new CommandDefinition(sql, new { UserId = userId }, cancellationToken: ct));
    }

    public async Task<(bool IsLocked, int RemainingMinutes)> CheckLockoutAsync(string username, CancellationToken ct)
    {
        const string sql = @"
            SELECT lockout_until_utc FROM ""user""
            WHERE LOWER(username) = LOWER(@Username) AND enabled = true";

        using var conn = _factory.CreateConnection();
        var lockoutUntil = await conn.ExecuteScalarAsync<long?>(
            new CommandDefinition(sql, new { Username = username }, cancellationToken: ct));

        if (!lockoutUntil.HasValue)
            return (false, 0);

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        if (lockoutUntil.Value > now)
        {
            var remaining = (int)((lockoutUntil.Value - now) / 60000) + 1;
            return (true, remaining);
        }

        return (false, 0);
    }

    public async Task<int> GetFailedLoginCountAsync(string username, CancellationToken ct)
    {
        const string sql = @"
            SELECT COALESCE(failed_login_count, 0) FROM ""user""
            WHERE LOWER(username) = LOWER(@Username) AND enabled = true";

        using var conn = _factory.CreateConnection();
        return await conn.ExecuteScalarAsync<int>(
            new CommandDefinition(sql, new { Username = username }, cancellationToken: ct));
    }

    private async Task IncrementFailedLoginAsync(string userId, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        // Get current failed count
        const string selectSql = @"SELECT COALESCE(failed_login_count, 0) FROM ""user"" WHERE user_id = @UserId";
        using var conn = _factory.CreateConnection();
        var currentCount = await conn.ExecuteScalarAsync<int>(
            new CommandDefinition(selectSql, new { UserId = userId }, cancellationToken: ct));

        var newCount = currentCount + 1;

        // Set lockout if max attempts reached
        long? lockoutUntil = null;
        if (newCount >= MaxFailedAttempts)
        {
            lockoutUntil = now + (LockoutMinutes * 60 * 1000);
        }

        const string updateSql = @"
            UPDATE ""user"" SET failed_login_count = @Count, lockout_until_utc = @LockoutUntil
            WHERE user_id = @UserId";

        await conn.ExecuteAsync(new CommandDefinition(updateSql, new
        {
            UserId = userId,
            Count = newCount,
            LockoutUntil = lockoutUntil
        }, cancellationToken: ct));
    }

    private async Task ResetFailedLoginAsync(string userId, CancellationToken ct)
    {
        const string sql = @"
            UPDATE ""user"" SET failed_login_count = 0, lockout_until_utc = NULL
            WHERE user_id = @UserId";

        using var conn = _factory.CreateConnection();
        await conn.ExecuteAsync(new CommandDefinition(sql, new { UserId = userId }, cancellationToken: ct));
    }

    private async Task UpgradePasswordHashAsync(string userId, string password, CancellationToken ct)
    {
        var newHash = HashPassword(password);
        const string sql = @"UPDATE ""user"" SET password_hash = @PasswordHash WHERE user_id = @UserId";

        using var conn = _factory.CreateConnection();
        await conn.ExecuteAsync(new CommandDefinition(sql, new { PasswordHash = newHash, UserId = userId }, cancellationToken: ct));
    }

    private static bool VerifyLegacyPassword(string password, string hash)
    {
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        var bytes = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(password));
        var inputHash = Convert.ToBase64String(bytes);
        return inputHash == hash;
    }

    private static string HashPassword(string password)
    {
        return BCrypt.Net.BCrypt.HashPassword(password, BCrypt.Net.BCrypt.GenerateSalt(12));
    }

    private static UserDto MapToDto(UserRow row) => new()
    {
        UserId = row.user_id,
        Username = row.username,
        DisplayName = row.display_name,
        Role = row.role,
        Enabled = row.enabled,
        CreatedUtc = row.created_utc,
        LastLoginUtc = row.last_login_utc,
        MustChangePassword = row.must_change_password
    };

    private sealed class UserRow
    {
        public string user_id { get; set; } = "";
        public string username { get; set; } = "";
        public string? display_name { get; set; }
        public string role { get; set; } = "";
        public bool enabled { get; set; }
        public long created_utc { get; set; }
        public long? last_login_utc { get; set; }
        public bool must_change_password { get; set; }
    }

    private sealed class UserRowWithPassword
    {
        public string user_id { get; set; } = "";
        public string username { get; set; } = "";
        public string? display_name { get; set; }
        public string role { get; set; } = "";
        public bool enabled { get; set; }
        public long created_utc { get; set; }
        public long? last_login_utc { get; set; }
        public string password_hash { get; set; } = "";
        public int failed_login_count { get; set; }
        public long? lockout_until_utc { get; set; }
        public bool must_change_password { get; set; }
    }

    private sealed class UserRowWithExpiry
    {
        public string user_id { get; set; } = "";
        public string username { get; set; } = "";
        public string? display_name { get; set; }
        public string role { get; set; } = "";
        public bool enabled { get; set; }
        public long created_utc { get; set; }
        public long? last_login_utc { get; set; }
        public long? refresh_token_expires_utc { get; set; }
        public bool must_change_password { get; set; }
    }
}
