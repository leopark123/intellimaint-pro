using System.Text.RegularExpressions;
using IntelliMaint.Core.Contracts;

namespace IntelliMaint.Host.Api.Validators;

/// <summary>
/// v56.1: 全局输入验证器 - 防止注入和无效输入
/// P1: 使用 SystemConstants 消除魔法数字
/// </summary>
public static partial class InputValidator
{
    // 用户名: 3-50字符, 只允许字母数字和下划线
    private static readonly Regex UsernameRegex = UsernamePattern();

    // 设备/标签ID: 1-100字符, 字母数字、下划线、连字符、点号
    private static readonly Regex IdentifierRegex = IdentifierPattern();

    // 危险字符: 可能用于注入的字符
    private static readonly Regex DangerousCharsRegex = DangerousCharsPattern();

    /// <summary>
    /// 验证用户名格式
    /// </summary>
    public static ValidationResult ValidateUsername(string? username)
    {
        if (string.IsNullOrWhiteSpace(username))
            return ValidationResult.Fail("用户名不能为空");

        if (username.Length < SystemConstants.Validation.UsernameMinLength)
            return ValidationResult.Fail($"用户名至少{SystemConstants.Validation.UsernameMinLength}个字符");

        if (username.Length > SystemConstants.Validation.UsernameMaxLength)
            return ValidationResult.Fail($"用户名不能超过{SystemConstants.Validation.UsernameMaxLength}个字符");

        if (!UsernameRegex.IsMatch(username))
            return ValidationResult.Fail("用户名只能包含字母、数字和下划线");

        return ValidationResult.Ok();
    }

    /// <summary>
    /// 验证密码强度
    /// </summary>
    public static ValidationResult ValidatePassword(string? password)
    {
        if (string.IsNullOrWhiteSpace(password))
            return ValidationResult.Fail("密码不能为空");

        if (password.Length < SystemConstants.Validation.PasswordMinLength)
            return ValidationResult.Fail($"密码至少{SystemConstants.Validation.PasswordMinLength}个字符");

        if (password.Length > SystemConstants.Validation.PasswordMaxLength)
            return ValidationResult.Fail($"密码不能超过{SystemConstants.Validation.PasswordMaxLength}个字符");

        var hasLetter = password.Any(char.IsLetter);
        var hasDigit = password.Any(char.IsDigit);

        if (!hasLetter || !hasDigit)
            return ValidationResult.Fail("密码必须包含字母和数字");

        return ValidationResult.Ok();
    }

    /// <summary>
    /// 验证设备/标签等标识符
    /// </summary>
    public static ValidationResult ValidateIdentifier(string? id, string fieldName = "标识符")
    {
        if (string.IsNullOrWhiteSpace(id))
            return ValidationResult.Fail($"{fieldName}不能为空");

        if (id.Length > 100)
            return ValidationResult.Fail($"{fieldName}不能超过100个字符");

        if (!IdentifierRegex.IsMatch(id))
            return ValidationResult.Fail($"{fieldName}格式无效，只能包含字母、数字、下划线、连字符和点号");

        return ValidationResult.Ok();
    }

    /// <summary>
    /// 验证显示名称（允许中文和空格）
    /// </summary>
    public static ValidationResult ValidateDisplayName(string? name, string fieldName = "名称")
    {
        if (string.IsNullOrWhiteSpace(name))
            return ValidationResult.Fail($"{fieldName}不能为空");

        if (name.Length > SystemConstants.Validation.NameMaxLength)
            return ValidationResult.Fail($"{fieldName}不能超过{SystemConstants.Validation.NameMaxLength}个字符");

        // 检查危险字符
        if (DangerousCharsRegex.IsMatch(name))
            return ValidationResult.Fail($"{fieldName}包含不允许的字符");

        return ValidationResult.Ok();
    }

    /// <summary>
    /// 验证可选的显示名称
    /// </summary>
    public static ValidationResult ValidateOptionalDisplayName(string? name, string fieldName = "名称")
    {
        if (string.IsNullOrWhiteSpace(name))
            return ValidationResult.Ok();

        return ValidateDisplayName(name, fieldName);
    }

    /// <summary>
    /// 验证描述文本
    /// </summary>
    public static ValidationResult ValidateDescription(string? description)
    {
        if (string.IsNullOrWhiteSpace(description))
            return ValidationResult.Ok();

        if (description.Length > SystemConstants.Validation.DescriptionMaxLength)
            return ValidationResult.Fail($"描述不能超过{SystemConstants.Validation.DescriptionMaxLength}个字符");

        if (DangerousCharsRegex.IsMatch(description))
            return ValidationResult.Fail("描述包含不允许的字符");

        return ValidationResult.Ok();
    }

    /// <summary>
    /// 验证分页参数
    /// </summary>
    public static ValidationResult ValidatePagination(int? page, int? pageSize)
    {
        if (page.HasValue && page.Value < 1)
            return ValidationResult.Fail("页码必须大于0");

        if (pageSize.HasValue)
        {
            if (pageSize.Value < 1)
                return ValidationResult.Fail("每页条数必须大于0");

            if (pageSize.Value > SystemConstants.Query.MaxLimit)
                return ValidationResult.Fail($"每页条数不能超过{SystemConstants.Query.MaxLimit}");
        }

        return ValidationResult.Ok();
    }

    /// <summary>
    /// 验证时间范围
    /// </summary>
    public static ValidationResult ValidateTimeRange(long? startTs, long? endTs)
    {
        if (startTs.HasValue && endTs.HasValue && startTs.Value > endTs.Value)
            return ValidationResult.Fail("开始时间不能大于结束时间");

        // 检查时间戳是否在合理范围内（2000年 - 2100年）
        const long minTs = 946684800000; // 2000-01-01
        const long maxTs = 4102444800000; // 2100-01-01

        if (startTs.HasValue && (startTs.Value < minTs || startTs.Value > maxTs))
            return ValidationResult.Fail("开始时间超出合理范围");

        if (endTs.HasValue && (endTs.Value < minTs || endTs.Value > maxTs))
            return ValidationResult.Fail("结束时间超出合理范围");

        return ValidationResult.Ok();
    }

    /// <summary>
    /// 组合多个验证结果
    /// </summary>
    public static ValidationResult Combine(params ValidationResult[] results)
    {
        var errors = results.Where(r => !r.IsValid).Select(r => r.Error).ToList();
        return errors.Count == 0
            ? ValidationResult.Ok()
            : ValidationResult.Fail(string.Join("; ", errors));
    }

    [GeneratedRegex(@"^[a-zA-Z0-9_]+$")]
    private static partial Regex UsernamePattern();

    [GeneratedRegex(@"^[a-zA-Z0-9_\-\.]+$")]
    private static partial Regex IdentifierPattern();

    [GeneratedRegex(@"[<>""';&\|`\$]")]
    private static partial Regex DangerousCharsPattern();
}

/// <summary>
/// 验证结果
/// </summary>
public readonly struct ValidationResult
{
    public bool IsValid { get; }
    public string? Error { get; }

    private ValidationResult(bool isValid, string? error)
    {
        IsValid = isValid;
        Error = error;
    }

    public static ValidationResult Ok() => new(true, null);
    public static ValidationResult Fail(string error) => new(false, error);

    /// <summary>
    /// 如果验证失败，返回 BadRequest 结果
    /// </summary>
    public IResult? ToBadRequestIfInvalid()
    {
        return IsValid ? null : Results.BadRequest(new { error = Error });
    }
}
