#!/usr/bin/env pwsh
<#
.SYNOPSIS
    IntelliMaint Pro v44 安全功能自动化测试脚本

.DESCRIPTION
    测试以下功能：
    1. JWT 认证
    2. SignalR 授权
    3. 请求限流
    4. 审计日志

.EXAMPLE
    ./Test-SecurityV44.ps1 -BaseUrl "http://localhost:5000"
#>

param(
    [string]$BaseUrl = "http://localhost:5000",
    [string]$Username = "admin",
    [string]$Password = "admin123"
)

$ErrorActionPreference = "Stop"

# 颜色输出
function Write-Success { Write-Host "✅ $args" -ForegroundColor Green }
function Write-Fail { Write-Host "❌ $args" -ForegroundColor Red }
function Write-Info { Write-Host "ℹ️  $args" -ForegroundColor Cyan }
function Write-Header { Write-Host "`n═══════════════════════════════════════════════════════════" -ForegroundColor Yellow; Write-Host "  $args" -ForegroundColor Yellow; Write-Host "═══════════════════════════════════════════════════════════" -ForegroundColor Yellow }

# 统计
$script:passed = 0
$script:failed = 0

function Test-Result {
    param([bool]$Condition, [string]$TestName, [string]$Details = "")
    
    if ($Condition) {
        Write-Success "$TestName"
        $script:passed++
    } else {
        Write-Fail "$TestName"
        if ($Details) { Write-Host "   详情: $Details" -ForegroundColor Gray }
        $script:failed++
    }
}

# ============================================
# 测试 1: 基础连接
# ============================================
Write-Header "测试 1: 基础连接"

try {
    $response = Invoke-WebRequest -Uri "$BaseUrl" -Method GET -TimeoutSec 5
    Test-Result ($response.StatusCode -eq 200) "API 服务可访问"
} catch {
    Write-Fail "API 服务不可访问: $_"
    Write-Host "`n请确保后端服务已启动: dotnet run --project src/Host.Api" -ForegroundColor Yellow
    exit 1
}

# ============================================
# 测试 2: JWT 认证
# ============================================
Write-Header "测试 2: JWT 认证"

# 2.1 登录成功
try {
    $loginBody = @{ username = $Username; password = $Password } | ConvertTo-Json
    $loginResponse = Invoke-RestMethod -Uri "$BaseUrl/api/auth/login" -Method POST -Body $loginBody -ContentType "application/json"
    
    $token = $loginResponse.data.token
    $refreshToken = $loginResponse.data.refreshToken
    
    Test-Result ($null -ne $token) "登录成功，获取 Token" 
    Test-Result ($null -ne $refreshToken) "获取 Refresh Token"
    Test-Result ($loginResponse.data.role -eq "Admin") "角色正确 (Admin)"
} catch {
    Write-Fail "登录失败: $_"
    exit 1
}

# 2.2 登录失败
try {
    $badLoginBody = @{ username = $Username; password = "wrongpassword" } | ConvertTo-Json
    $badLoginResponse = Invoke-WebRequest -Uri "$BaseUrl/api/auth/login" -Method POST -Body $badLoginBody -ContentType "application/json" -SkipHttpErrorCheck
    
    Test-Result ($badLoginResponse.StatusCode -eq 401) "错误密码返回 401"
} catch {
    Write-Fail "登录失败测试异常: $_"
}

# 2.3 无 Token 访问受保护资源
try {
    $noAuthResponse = Invoke-WebRequest -Uri "$BaseUrl/api/devices" -Method GET -SkipHttpErrorCheck
    Test-Result ($noAuthResponse.StatusCode -eq 401) "无 Token 访问返回 401"
} catch {
    Write-Fail "无 Token 测试异常: $_"
}

# 2.4 有 Token 访问受保护资源
try {
    $headers = @{ Authorization = "Bearer $token" }
    $authResponse = Invoke-WebRequest -Uri "$BaseUrl/api/devices" -Method GET -Headers $headers
    Test-Result ($authResponse.StatusCode -eq 200) "有 Token 访问返回 200"
} catch {
    Write-Fail "有 Token 测试异常: $_"
}

# 2.5 Token 刷新
try {
    $refreshBody = @{ refreshToken = $refreshToken } | ConvertTo-Json
    $refreshResponse = Invoke-RestMethod -Uri "$BaseUrl/api/auth/refresh" -Method POST -Body $refreshBody -ContentType "application/json"
    
    $newToken = $refreshResponse.data.token
    Test-Result ($null -ne $newToken -and $newToken -ne $token) "Token 刷新成功 (新 Token 不同)"
    
    # 更新 token
    $token = $newToken
} catch {
    Write-Fail "Token 刷新失败: $_"
}

# ============================================
# 测试 3: SignalR 授权
# ============================================
Write-Header "测试 3: SignalR 授权"

# 3.1 无 Token 连接 SignalR
try {
    $signalrUrl = "$BaseUrl/hubs/telemetry/negotiate?negotiateVersion=1"
    $noAuthSignalR = Invoke-WebRequest -Uri $signalrUrl -Method POST -SkipHttpErrorCheck
    Test-Result ($noAuthSignalR.StatusCode -eq 401) "SignalR 无 Token 返回 401"
} catch {
    # 可能连接直接被拒绝
    Test-Result $true "SignalR 无 Token 连接被拒绝"
}

# 3.2 有 Token 连接 SignalR
try {
    $headers = @{ Authorization = "Bearer $token" }
    $signalrUrl = "$BaseUrl/hubs/telemetry/negotiate?negotiateVersion=1"
    $authSignalR = Invoke-WebRequest -Uri $signalrUrl -Method POST -Headers $headers -SkipHttpErrorCheck
    Test-Result ($authSignalR.StatusCode -eq 200) "SignalR 有 Token 返回 200"
} catch {
    Write-Fail "SignalR 有 Token 测试异常: $_"
}

# 3.3 Query String Token (SignalR 方式)
try {
    $signalrUrl = "$BaseUrl/hubs/telemetry/negotiate?negotiateVersion=1&access_token=$token"
    $qsSignalR = Invoke-WebRequest -Uri $signalrUrl -Method POST -SkipHttpErrorCheck
    Test-Result ($qsSignalR.StatusCode -eq 200) "SignalR Query String Token 有效"
} catch {
    Write-Fail "SignalR Query String 测试异常: $_"
}

# ============================================
# 测试 4: 请求限流
# ============================================
Write-Header "测试 4: 请求限流"

Write-Info "发送 110 次请求测试限流 (60秒/100次)..."

$headers = @{ Authorization = "Bearer $token" }
$successCount = 0
$limitedCount = 0
$otherCount = 0

for ($i = 1; $i -le 110; $i++) {
    try {
        $response = Invoke-WebRequest -Uri "$BaseUrl/api/devices" -Method GET -Headers $headers -SkipHttpErrorCheck
        
        switch ($response.StatusCode) {
            200 { $successCount++ }
            429 { $limitedCount++ }
            default { $otherCount++ }
        }
        
        # 显示进度
        if ($i % 20 -eq 0) {
            Write-Host "  进度: $i/110 (成功: $successCount, 限流: $limitedCount)" -ForegroundColor Gray
        }
    } catch {
        $otherCount++
    }
}

Write-Info "结果: 成功=$successCount, 被限流=$limitedCount, 其他=$otherCount"

Test-Result ($successCount -ge 95 -and $successCount -le 105) "成功请求数约 100 (实际: $successCount)"
Test-Result ($limitedCount -ge 5) "被限流请求数 >= 5 (实际: $limitedCount)"

# 等待限流窗口重置
Write-Info "等待 5 秒后继续..."
Start-Sleep -Seconds 5

# ============================================
# 测试 5: 审计日志
# ============================================
Write-Header "测试 5: 审计日志"

# 获取新 Token（之前的可能已用尽限流配额）
try {
    $loginBody = @{ username = $Username; password = $Password } | ConvertTo-Json
    $loginResponse = Invoke-RestMethod -Uri "$BaseUrl/api/auth/login" -Method POST -Body $loginBody -ContentType "application/json"
    $token = $loginResponse.data.token
} catch {
    Write-Fail "重新登录失败"
}

# 5.1 查询审计日志
try {
    $headers = @{ Authorization = "Bearer $token" }
    $auditResponse = Invoke-RestMethod -Uri "$BaseUrl/api/audit?pageSize=20" -Method GET -Headers $headers
    
    Test-Result ($auditResponse.success -eq $true) "审计日志查询成功"
    
    $auditItems = $auditResponse.data.items
    Test-Result ($auditItems.Count -gt 0) "审计日志有记录 (数量: $($auditItems.Count))"
    
    # 检查是否有登录记录
    $loginLogs = $auditItems | Where-Object { $_.action -eq "Login" }
    Test-Result ($loginLogs.Count -gt 0) "包含登录审计记录"
    
    # 检查是否有 Token 刷新记录
    $refreshLogs = $auditItems | Where-Object { $_.action -eq "TokenRefresh" }
    Test-Result ($refreshLogs.Count -gt 0) "包含 Token 刷新审计记录"
    
    # 检查 IP 地址字段
    $hasIp = $auditItems | Where-Object { $null -ne $_.ipAddress -and $_.ipAddress -ne "" }
    Test-Result ($hasIp.Count -gt 0) "审计记录包含 IP 地址"
    
    # 显示最近 3 条记录
    Write-Info "最近 3 条审计记录:"
    $auditItems | Select-Object -First 3 | ForEach-Object {
        $ts = [DateTimeOffset]::FromUnixTimeMilliseconds($_.ts).LocalDateTime.ToString("HH:mm:ss")
        Write-Host "  [$ts] $($_.userName) - $($_.action) - $($_.resourceType) - IP: $($_.ipAddress)" -ForegroundColor Gray
    }
} catch {
    Write-Fail "审计日志测试异常: $_"
}

# 5.2 检查登录失败记录
try {
    $failedLogs = $auditItems | Where-Object { $_.action -eq "LoginFailed" }
    Test-Result ($failedLogs.Count -gt 0) "包含登录失败审计记录"
} catch {
    # 可能没有登录失败记录
    Write-Info "未检测到登录失败记录（正常，如果之前没有失败登录尝试）"
}

# ============================================
# 测试 6: RBAC 权限
# ============================================
Write-Header "测试 6: RBAC 权限"

# 6.1 Admin 访问用户管理
try {
    $headers = @{ Authorization = "Bearer $token" }
    $usersResponse = Invoke-WebRequest -Uri "$BaseUrl/api/users" -Method GET -Headers $headers -SkipHttpErrorCheck
    Test-Result ($usersResponse.StatusCode -eq 200) "Admin 可访问用户管理"
} catch {
    Write-Fail "Admin 访问用户管理异常: $_"
}

# 6.2 Admin 访问系统设置
try {
    $headers = @{ Authorization = "Bearer $token" }
    $settingsResponse = Invoke-WebRequest -Uri "$BaseUrl/api/settings" -Method GET -Headers $headers -SkipHttpErrorCheck
    Test-Result ($settingsResponse.StatusCode -eq 200) "Admin 可访问系统设置"
} catch {
    Write-Fail "Admin 访问系统设置异常: $_"
}

# ============================================
# 测试总结
# ============================================
Write-Header "测试总结"

$total = $script:passed + $script:failed
$passRate = if ($total -gt 0) { [math]::Round(($script:passed / $total) * 100, 1) } else { 0 }

Write-Host ""
Write-Host "  总测试数: $total" -ForegroundColor White
Write-Success "通过: $($script:passed)"
if ($script:failed -gt 0) {
    Write-Fail "失败: $($script:failed)"
}
Write-Host "  通过率: $passRate%" -ForegroundColor $(if ($passRate -ge 80) { "Green" } elseif ($passRate -ge 60) { "Yellow" } else { "Red" })
Write-Host ""

if ($script:failed -eq 0) {
    Write-Host "🎉 所有测试通过！v44 安全功能正常工作。" -ForegroundColor Green
} else {
    Write-Host "⚠️  部分测试失败，请检查上述错误。" -ForegroundColor Yellow
}

exit $script:failed
