# v56 功能测试脚本
# 测试离线检测 + 变化率告警

$baseUrl = "http://localhost:5000"

Write-Host "=== v56 功能测试 ===" -ForegroundColor Cyan

# 1. 登录
Write-Host "`n[1] 登录获取 Token..." -ForegroundColor Yellow
$loginBody = @{
    username = "admin"
    password = "admin123"
} | ConvertTo-Json

$loginResponse = Invoke-RestMethod -Uri "$baseUrl/api/auth/login" -Method Post -Body $loginBody -ContentType "application/json"
$token = $loginResponse.data.accessToken
Write-Host "登录成功! Token: $($token.Substring(0, 20))..." -ForegroundColor Green

$headers = @{
    "Authorization" = "Bearer $token"
    "Content-Type" = "application/json"
}

# 2. 创建离线检测规则
Write-Host "`n[2] 创建离线检测规则..." -ForegroundColor Yellow
$offlineRule = @{
    ruleId = "offline-test-$(Get-Date -Format 'HHmmss')"
    name = "泵站离线检测"
    tagId = "pump1.status"
    conditionType = "offline"
    threshold = 30
    severity = 4
    messageTemplate = "[离线] {tagId} 已超过 {threshold} 秒无数据"
} | ConvertTo-Json

try {
    $result = Invoke-RestMethod -Uri "$baseUrl/api/alarm-rules" -Method Post -Body $offlineRule -Headers $headers
    Write-Host "创建成功!" -ForegroundColor Green
    Write-Host "  RuleId: $($result.data.ruleId)"
    Write-Host "  RuleType: $($result.data.ruleType)"
    Write-Host "  ConditionType: $($result.data.conditionType)"
} catch {
    Write-Host "创建失败: $_" -ForegroundColor Red
}

# 3. 创建变化率告警规则 (百分比)
Write-Host "`n[3] 创建变化率告警规则 (百分比)..." -ForegroundColor Yellow
$rocPercentRule = @{
    ruleId = "roc-percent-$(Get-Date -Format 'HHmmss')"
    name = "温度突变检测"
    tagId = "reactor.temperature"
    conditionType = "roc_percent"
    threshold = 15
    rocWindowMs = 300000
    severity = 3
    messageTemplate = "[变化率] {tagId} 在 5 分钟内变化 {changePercent}%"
} | ConvertTo-Json

try {
    $result = Invoke-RestMethod -Uri "$baseUrl/api/alarm-rules" -Method Post -Body $rocPercentRule -Headers $headers
    Write-Host "创建成功!" -ForegroundColor Green
    Write-Host "  RuleId: $($result.data.ruleId)"
    Write-Host "  RuleType: $($result.data.ruleType)"
    Write-Host "  RocWindowMs: $($result.data.rocWindowMs)"
} catch {
    Write-Host "创建失败: $_" -ForegroundColor Red
}

# 4. 创建变化率告警规则 (绝对值)
Write-Host "`n[4] 创建变化率告警规则 (绝对值)..." -ForegroundColor Yellow
$rocAbsoluteRule = @{
    ruleId = "roc-absolute-$(Get-Date -Format 'HHmmss')"
    name = "压力突变检测"
    tagId = "tank.pressure"
    conditionType = "roc_absolute"
    threshold = 50
    rocWindowMs = 60000
    severity = 4
} | ConvertTo-Json

try {
    $result = Invoke-RestMethod -Uri "$baseUrl/api/alarm-rules" -Method Post -Body $rocAbsoluteRule -Headers $headers
    Write-Host "创建成功!" -ForegroundColor Green
    Write-Host "  RuleId: $($result.data.ruleId)"
    Write-Host "  RuleType: $($result.data.ruleType)"
} catch {
    Write-Host "创建失败: $_" -ForegroundColor Red
}

# 5. 测试验证 - 无效的离线阈值
Write-Host "`n[5] 测试验证 - 无效的离线阈值 (应失败)..." -ForegroundColor Yellow
$invalidOffline = @{
    ruleId = "invalid-offline"
    name = "无效规则"
    tagId = "test.tag"
    conditionType = "offline"
    threshold = -10
} | ConvertTo-Json

try {
    $result = Invoke-RestMethod -Uri "$baseUrl/api/alarm-rules" -Method Post -Body $invalidOffline -Headers $headers
    Write-Host "意外成功 (应该失败)" -ForegroundColor Red
} catch {
    Write-Host "正确拒绝了无效请求!" -ForegroundColor Green
}

# 6. 测试验证 - 缺少 RocWindowMs
Write-Host "`n[6] 测试验证 - 缺少 RocWindowMs (应失败)..." -ForegroundColor Yellow
$missingWindow = @{
    ruleId = "missing-window"
    name = "缺少窗口"
    tagId = "test.tag"
    conditionType = "roc_percent"
    threshold = 10
} | ConvertTo-Json

try {
    $result = Invoke-RestMethod -Uri "$baseUrl/api/alarm-rules" -Method Post -Body $missingWindow -Headers $headers
    Write-Host "意外成功 (应该失败)" -ForegroundColor Red
} catch {
    Write-Host "正确拒绝了无效请求!" -ForegroundColor Green
}

# 7. 查询所有规则
Write-Host "`n[7] 查询所有告警规则..." -ForegroundColor Yellow
$rules = Invoke-RestMethod -Uri "$baseUrl/api/alarm-rules" -Method Get -Headers $headers

Write-Host "共 $($rules.data.Count) 条规则:" -ForegroundColor Green
foreach ($rule in $rules.data) {
    $typeIcon = switch ($rule.ruleType) {
        "offline" { "[离线]" }
        "roc" { "[变化率]" }
        default { "[阈值]" }
    }
    Write-Host "  $typeIcon $($rule.ruleId) - $($rule.name) ($($rule.conditionType))"
}

Write-Host "`n=== 测试完成 ===" -ForegroundColor Cyan
Write-Host "离线检测服务每 10 秒检查一次"
Write-Host "变化率告警在数据点到达时实时评估"
