using IntelliMaint.Core.Contracts;

namespace IntelliMaint.Core.Abstractions;

/// <summary>
/// Token 生成服务接口（由 Infrastructure 实现）
/// </summary>
public interface ITokenService
{
    (LoginResponse Response, long RefreshExpiresUtc) GenerateTokens(UserDto user);
}
