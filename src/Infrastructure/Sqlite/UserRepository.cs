using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IntelliMaint.Core.Abstractions;
using IntelliMaint.Core.Contracts;

namespace IntelliMaint.Infrastructure.Sqlite;

public sealed class UserRepository : IUserRepository
{
    private readonly IDbExecutor _db;
    
    // 账号锁定配置
    private const int MaxFailedAttempts = 5;
    private const int LockoutMinutes = 15;

    public UserRepository(IDbExecutor db)
    {
        _db = db;
    }

    public async Task<UserDto?> GetByUsernameAsync(string username, CancellationToken ct)
    {
        const string sql = @"
            SELECT user_id, username, display_name, role, enabled, created_utc, last_login_utc
            FROM user WHERE username = @Username COLLATE NOCASE";

        return await _db.QuerySingleAsync(sql, reader => new UserDto
        {
            UserId = reader.GetString(0),
            Username = reader.GetString(1),
            DisplayName = reader.IsDBNull(2) ? null : reader.GetString(2),
            Role = reader.GetString(3),
            Enabled = reader.GetInt64(4) == 1,
            CreatedUtc = reader.GetInt64(5),
            LastLoginUtc = reader.IsDBNull(6) ? null : reader.GetInt64(6)
        }, new { Username = username }, ct);
    }

    public async Task<UserDto?> GetByIdAsync(string userId, CancellationToken ct)
    {
        const string sql = @"
            SELECT user_id, username, display_name, role, enabled, created_utc, last_login_utc
            FROM user WHERE user_id = @UserId";

        return await _db.QuerySingleAsync(sql, reader => new UserDto
        {
            UserId = reader.GetString(0),
            Username = reader.GetString(1),
            DisplayName = reader.IsDBNull(2) ? null : reader.GetString(2),
            Role = reader.GetString(3),
            Enabled = reader.GetInt64(4) == 1,
            CreatedUtc = reader.GetInt64(5),
            LastLoginUtc = reader.IsDBNull(6) ? null : reader.GetInt64(6)
        }, new { UserId = userId }, ct);
    }

    public async Task<UserDto?> ValidateCredentialsAsync(string username, string password, CancellationToken ct)
    {
        const string sql = @"
            SELECT user_id, username, display_name, role, enabled, created_utc, last_login_utc, password_hash,
                   failed_login_count, lockout_until_utc
            FROM user WHERE username = @Username COLLATE NOCASE AND enabled = 1";

        var result = await _db.QueryAsync(sql, reader => new
        {
            User = new UserDto
            {
                UserId = reader.GetString(0),
                Username = reader.GetString(1),
                DisplayName = reader.IsDBNull(2) ? null : reader.GetString(2),
                Role = reader.GetString(3),
                Enabled = reader.GetInt64(4) == 1,
                CreatedUtc = reader.GetInt64(5),
                LastLoginUtc = reader.IsDBNull(6) ? null : reader.GetInt64(6)
            },
            PasswordHash = reader.GetString(7),
            FailedLoginCount = reader.IsDBNull(8) ? 0 : reader.GetInt32(8),
            LockoutUntilUtc = reader.IsDBNull(9) ? (long?)null : reader.GetInt64(9)
        }, new { Username = username }, ct);

        if (result.Count == 0)
            return null;

        var record = result[0];
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        // P0-2: 检查账号是否被锁定
        if (record.LockoutUntilUtc.HasValue && record.LockoutUntilUtc.Value > now)
        {
            return null; // 账号被锁定
        }

        // P0-1: 使用 BCrypt 验证密码
        bool passwordValid;
        try
        {
            // 尝试 BCrypt 验证（新格式）
            passwordValid = BCrypt.Net.BCrypt.Verify(password, record.PasswordHash);
        }
        catch
        {
            // 如果失败，尝试旧的 SHA256 格式（兼容迁移）
            passwordValid = VerifyLegacyPassword(password, record.PasswordHash);
            
            // 如果旧格式验证成功，升级到 BCrypt
            if (passwordValid)
            {
                await UpgradePasswordHashAsync(record.User.UserId, password, ct);
            }
        }

        if (!passwordValid)
        {
            // P0-2: 记录失败次数
            await IncrementFailedLoginAsync(record.User.UserId, ct);
            return null;
        }

        // P0-2: 登录成功，重置失败计数
        await ResetFailedLoginAsync(record.User.UserId, ct);
        
        return record.User;
    }
    
    /// <summary>检查账号是否被锁定</summary>
    public async Task<(bool IsLocked, int RemainingMinutes)> CheckLockoutAsync(string username, CancellationToken ct)
    {
        const string sql = @"
            SELECT lockout_until_utc FROM user 
            WHERE username = @Username COLLATE NOCASE AND enabled = 1";

        var result = await _db.QueryAsync(sql, reader => 
            reader.IsDBNull(0) ? (long?)null : reader.GetInt64(0), 
            new { Username = username }, ct);

        if (result.Count == 0 || !result[0].HasValue)
            return (false, 0);

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var lockoutUntil = result[0]!.Value;

        if (lockoutUntil > now)
        {
            var remaining = (int)((lockoutUntil - now) / 60000) + 1;
            return (true, remaining);
        }

        return (false, 0);
    }
    
    /// <summary>获取失败登录次数</summary>
    public async Task<int> GetFailedLoginCountAsync(string username, CancellationToken ct)
    {
        const string sql = @"
            SELECT failed_login_count FROM user 
            WHERE username = @Username COLLATE NOCASE AND enabled = 1";

        var result = await _db.QueryAsync(sql, reader => 
            reader.IsDBNull(0) ? 0 : reader.GetInt32(0), 
            new { Username = username }, ct);

        return result.Count > 0 ? result[0] : 0;
    }
    
    private async Task IncrementFailedLoginAsync(string userId, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        
        // 先获取当前失败次数
        const string selectSql = "SELECT failed_login_count FROM user WHERE user_id = @UserId";
        var result = await _db.QueryAsync(selectSql, reader => 
            reader.IsDBNull(0) ? 0 : reader.GetInt32(0), 
            new { UserId = userId }, ct);
        
        var currentCount = result.Count > 0 ? result[0] : 0;
        var newCount = currentCount + 1;
        
        // 如果达到最大次数，设置锁定时间
        long? lockoutUntil = null;
        if (newCount >= MaxFailedAttempts)
        {
            lockoutUntil = now + (LockoutMinutes * 60 * 1000);
        }
        
        const string updateSql = @"
            UPDATE user SET 
                failed_login_count = @Count,
                lockout_until_utc = @LockoutUntil
            WHERE user_id = @UserId";
        
        await _db.ExecuteNonQueryAsync(updateSql, new 
        { 
            UserId = userId, 
            Count = newCount,
            LockoutUntil = lockoutUntil
        }, ct);
    }
    
    private async Task ResetFailedLoginAsync(string userId, CancellationToken ct)
    {
        const string sql = @"
            UPDATE user SET 
                failed_login_count = 0,
                lockout_until_utc = NULL
            WHERE user_id = @UserId";
        
        await _db.ExecuteNonQueryAsync(sql, new { UserId = userId }, ct);
    }
    
    private async Task UpgradePasswordHashAsync(string userId, string password, CancellationToken ct)
    {
        var newHash = HashPassword(password);
        const string sql = "UPDATE user SET password_hash = @PasswordHash WHERE user_id = @UserId";
        await _db.ExecuteNonQueryAsync(sql, new { PasswordHash = newHash, UserId = userId }, ct);
    }
    
    /// <summary>验证旧的 SHA256 密码格式（用于迁移）</summary>
    private static bool VerifyLegacyPassword(string password, string hash)
    {
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        var bytes = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(password));
        var inputHash = Convert.ToBase64String(bytes);
        return inputHash == hash;
    }

    public async Task<UserDto?> CreateAsync(string username, string password, string role, string? displayName, CancellationToken ct)
    {
        var userId = Guid.NewGuid().ToString("N")[..16];
        var passwordHash = HashPassword(password);
        var createdUtc = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        const string sql = @"
            INSERT INTO user (user_id, username, password_hash, display_name, role, enabled, created_utc)
            VALUES (@UserId, @Username, @PasswordHash, @DisplayName, @Role, 1, @CreatedUtc)";

        await _db.ExecuteNonQueryAsync(sql, new
        {
            UserId = userId,
            Username = username,
            PasswordHash = passwordHash,
            DisplayName = displayName,
            Role = role,
            CreatedUtc = createdUtc
        }, ct);

        return new UserDto
        {
            UserId = userId,
            Username = username,
            DisplayName = displayName,
            Role = role,
            Enabled = true,
            CreatedUtc = createdUtc
        };
    }

    public async Task<UserDto?> UpdateAsync(string userId, string? displayName, string? role, bool? enabled, CancellationToken ct)
    {
        // 动态构建 SET 子句
        var sets = new List<string>();
        var parameters = new Dictionary<string, object> { { "UserId", userId } };

        if (displayName != null)
        {
            sets.Add("display_name = @DisplayName");
            parameters["DisplayName"] = displayName;
        }
        if (role != null)
        {
            sets.Add("role = @Role");
            parameters["Role"] = role;
        }
        if (enabled.HasValue)
        {
            sets.Add("enabled = @Enabled");
            parameters["Enabled"] = enabled.Value ? 1 : 0;
        }

        if (sets.Count == 0)
        {
            // 没有需要更新的字段，直接返回当前用户
            return await GetByIdAsync(userId, ct);
        }

        var sql = $"UPDATE user SET {string.Join(", ", sets)} WHERE user_id = @UserId";
        await _db.ExecuteNonQueryAsync(sql, parameters, ct);

        return await GetByIdAsync(userId, ct);
    }

    public async Task<bool> UpdatePasswordAsync(string userId, string currentPassword, string newPassword, CancellationToken ct)
    {
        // 先验证当前密码
        const string selectSql = "SELECT password_hash FROM user WHERE user_id = @UserId AND enabled = 1";
        var result = await _db.QueryAsync(selectSql, reader => reader.GetString(0), new { UserId = userId }, ct);

        if (result.Count == 0)
            return false;

        var currentHash = HashPassword(currentPassword);
        if (result[0] != currentHash)
            return false;

        // 更新密码
        var newHash = HashPassword(newPassword);
        const string updateSql = "UPDATE user SET password_hash = @PasswordHash WHERE user_id = @UserId";
        var affected = await _db.ExecuteNonQueryAsync(updateSql, new { PasswordHash = newHash, UserId = userId }, ct);

        return affected > 0;
    }

    public async Task<bool> ResetPasswordAsync(string userId, string newPassword, CancellationToken ct)
    {
        var newHash = HashPassword(newPassword);
        const string sql = "UPDATE user SET password_hash = @PasswordHash WHERE user_id = @UserId";
        var affected = await _db.ExecuteNonQueryAsync(sql, new { PasswordHash = newHash, UserId = userId }, ct);

        return affected > 0;
    }

    public async Task<bool> DisableAsync(string userId, CancellationToken ct)
    {
        const string sql = "UPDATE user SET enabled = 0, refresh_token = NULL, refresh_token_expires_utc = NULL WHERE user_id = @UserId";
        var affected = await _db.ExecuteNonQueryAsync(sql, new { UserId = userId }, ct);

        return affected > 0;
    }

    public async Task UpdateLastLoginAsync(string userId, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        const string sql = "UPDATE user SET last_login_utc = @Now WHERE user_id = @UserId";

        await _db.ExecuteNonQueryAsync(sql, new { Now = now, UserId = userId }, ct);
    }

    public async Task<IReadOnlyList<UserDto>> ListAsync(CancellationToken ct)
    {
        const string sql = @"
            SELECT user_id, username, display_name, role, enabled, created_utc, last_login_utc
            FROM user ORDER BY created_utc DESC";

        var list = await _db.QueryAsync(sql, reader => new UserDto
        {
            UserId = reader.GetString(0),
            Username = reader.GetString(1),
            DisplayName = reader.IsDBNull(2) ? null : reader.GetString(2),
            Role = reader.GetString(3),
            Enabled = reader.GetInt64(4) == 1,
            CreatedUtc = reader.GetInt64(5),
            LastLoginUtc = reader.IsDBNull(6) ? null : reader.GetInt64(6)
        }, null, ct);

        return list;
    }

    /// <summary>使用 BCrypt 哈希密码</summary>
    private static string HashPassword(string password)
    {
        // BCrypt 自动生成盐值并包含在哈希中
        return BCrypt.Net.BCrypt.HashPassword(password, BCrypt.Net.BCrypt.GenerateSalt(12));
    }

    public async Task SaveRefreshTokenAsync(string userId, string refreshToken, long expiresUtc, CancellationToken ct)
    {
        const string sql = @"
            UPDATE user 
            SET refresh_token = @RefreshToken, 
                refresh_token_expires_utc = @ExpiresUtc 
            WHERE user_id = @UserId";

        await _db.ExecuteNonQueryAsync(sql, new 
        { 
            UserId = userId, 
            RefreshToken = refreshToken, 
            ExpiresUtc = expiresUtc 
        }, ct);
    }

    public async Task<UserDto?> GetByRefreshTokenAsync(string refreshToken, CancellationToken ct)
    {
        const string sql = @"
            SELECT user_id, username, display_name, role, enabled, created_utc, last_login_utc,
                   refresh_token_expires_utc
            FROM user 
            WHERE refresh_token = @RefreshToken 
              AND enabled = 1";

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        var result = await _db.QueryAsync(sql, reader =>
        {
            var expiresUtc = reader.IsDBNull(7) ? 0L : reader.GetInt64(7);

            // 检查是否过期
            if (expiresUtc < now)
                return null;

            return new UserDto
            {
                UserId = reader.GetString(0),
                Username = reader.GetString(1),
                DisplayName = reader.IsDBNull(2) ? null : reader.GetString(2),
                Role = reader.GetString(3),
                Enabled = reader.GetInt64(4) == 1,
                CreatedUtc = reader.GetInt64(5),
                LastLoginUtc = reader.IsDBNull(6) ? null : reader.GetInt64(6)
            };
        }, new { RefreshToken = refreshToken }, ct);

        return result.FirstOrDefault(u => u != null);
    }

    public async Task ClearRefreshTokenAsync(string userId, CancellationToken ct)
    {
        const string sql = @"
            UPDATE user 
            SET refresh_token = NULL, 
                refresh_token_expires_utc = NULL 
            WHERE user_id = @UserId";

        await _db.ExecuteNonQueryAsync(sql, new { UserId = userId }, ct);
    }
}
