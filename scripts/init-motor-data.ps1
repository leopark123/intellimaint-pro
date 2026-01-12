# 电机故障预测系统 - 演示数据初始化脚本
# 使用 API 创建电机模型、实例、参数映射和操作模式

$ErrorActionPreference = "Stop"
$ApiBase = "http://localhost:5000/api"

# 1. 登录获取 Token
Write-Host "=== 1. 登录获取 Token ===" -ForegroundColor Cyan
$loginBody = @{
    username = "admin"
    password = "admin123"
} | ConvertTo-Json

$loginResult = Invoke-RestMethod -Uri "$ApiBase/auth/login" -Method Post -Body $loginBody -ContentType "application/json"
$token = $loginResult.token
Write-Host "Token obtained: $($token.Substring(0, 50))..." -ForegroundColor Green

$headers = @{
    "Authorization" = "Bearer $token"
    "Content-Type" = "application/json"
}

# 2. 创建电机模型
Write-Host "`n=== 2. 创建电机模型 ===" -ForegroundColor Cyan

$motorModels = @(
    @{
        name = "标准感应电机 15kW"
        description = "三相异步感应电机，适用于一般工业驱动"
        type = 0  # InductionMotor
        ratedPower = 15.0
        ratedVoltage = 380
        ratedCurrent = 30
        ratedSpeed = 1480
        ratedFrequency = 50
        polePairs = 2
        vfdModel = "ABB ACS580"
        bearingModel = "SKF 6308"
        bearingRollingElements = 8
        bearingBallDiameter = 15.875
        bearingPitchDiameter = 58.5
        bearingContactAngle = 0
    },
    @{
        name = "变频调速电机 7.5kW"
        description = "变频专用电机，宽频调速"
        type = 0  # InductionMotor
        ratedPower = 7.5
        ratedVoltage = 380
        ratedCurrent = 15
        ratedSpeed = 1450
        ratedFrequency = 50
        polePairs = 2
        vfdModel = "Siemens G120"
        bearingModel = "SKF 6206"
        bearingRollingElements = 9
        bearingBallDiameter = 9.525
        bearingPitchDiameter = 46
        bearingContactAngle = 0
    }
)

$createdModels = @()
foreach ($model in $motorModels) {
    try {
        $result = Invoke-RestMethod -Uri "$ApiBase/motor-models" -Method Post -Headers $headers -Body ($model | ConvertTo-Json)
        $createdModels += $result
        Write-Host "  Created model: $($result.name) [$($result.modelId)]" -ForegroundColor Green
    } catch {
        Write-Host "  Error creating model $($model.name): $_" -ForegroundColor Yellow
    }
}

# 3. 获取现有设备
Write-Host "`n=== 3. 获取现有设备 ===" -ForegroundColor Cyan
$devices = Invoke-RestMethod -Uri "$ApiBase/devices" -Method Get -Headers $headers
Write-Host "  Found $($devices.Count) devices:" -ForegroundColor Green
foreach ($d in $devices) {
    Write-Host "    - $($d.deviceId): $($d.name)" -ForegroundColor Gray
}

# 4. 获取现有标签
Write-Host "`n=== 4. 获取现有标签 ===" -ForegroundColor Cyan
$tags = Invoke-RestMethod -Uri "$ApiBase/tags" -Method Get -Headers $headers
Write-Host "  Found $($tags.Count) tags" -ForegroundColor Green

# 5. 创建电机实例
Write-Host "`n=== 5. 创建电机实例 ===" -ForegroundColor Cyan

$motorInstances = @(
    @{
        modelId = $createdModels[0].modelId
        deviceId = "Motor-001"
        name = "1#主传动电机"
        location = "车间A-1号产线"
        installDate = "2024-01-15"
        assetNumber = "MTR-2024-001"
    },
    @{
        modelId = if ($createdModels.Count -gt 1) { $createdModels[1].modelId } else { $createdModels[0].modelId }
        deviceId = "SIM-PLC-001"
        name = "2#辅助电机"
        location = "车间A-2号产线"
        installDate = "2024-03-20"
        assetNumber = "MTR-2024-002"
    }
)

$createdInstances = @()
foreach ($instance in $motorInstances) {
    try {
        $result = Invoke-RestMethod -Uri "$ApiBase/motor-instances" -Method Post -Headers $headers -Body ($instance | ConvertTo-Json)
        $createdInstances += $result
        Write-Host "  Created instance: $($result.name) [$($result.instanceId)]" -ForegroundColor Green
    } catch {
        Write-Host "  Error creating instance $($instance.name): $_" -ForegroundColor Yellow
    }
}

# 6. 创建参数映射
Write-Host "`n=== 6. 创建参数映射 ===" -ForegroundColor Cyan

# 参数映射模板
$parameterMappings = @{
    "Motor-001" = @(
        @{ parameter = 40; tagId = "Motor1_Temp"; scaleFactor = 1.0; offset = 0; usedForDiagnosis = $true }      # Temperature
        @{ parameter = 3; tagId = "Motor1_Current"; scaleFactor = 1.0; offset = 0; usedForDiagnosis = $true }   # CurrentRMS
        @{ parameter = 21; tagId = "Motor1_Speed"; scaleFactor = 30; offset = 0; usedForDiagnosis = $true }     # Speed (RPM = value * 30)
        @{ parameter = 20; tagId = "Torque"; scaleFactor = 1.0; offset = 0; usedForDiagnosis = $true }          # Torque
    )
    "SIM-PLC-001" = @(
        @{ parameter = 40; tagId = "Motor1_Temp"; scaleFactor = 1.0; offset = 0; usedForDiagnosis = $true }      # Temperature
        @{ parameter = 3; tagId = "Motor1_Current"; scaleFactor = 1.0; offset = 0; usedForDiagnosis = $true }   # CurrentRMS
        @{ parameter = 21; tagId = "Motor1_Speed"; scaleFactor = 30; offset = 0; usedForDiagnosis = $true }     # Speed
        @{ parameter = 34; tagId = "Motor1_Running"; scaleFactor = 1.0; offset = 0; usedForDiagnosis = $false } # OperationState
    )
}

foreach ($instance in $createdInstances) {
    $deviceId = $instance.deviceId
    if ($parameterMappings.ContainsKey($deviceId)) {
        $mappings = $parameterMappings[$deviceId]
        try {
            $result = Invoke-RestMethod -Uri "$ApiBase/motor-instances/$($instance.instanceId)/mappings/batch" -Method Post -Headers $headers -Body ($mappings | ConvertTo-Json)
            Write-Host "  Created $($result.created) mappings for $($instance.name)" -ForegroundColor Green
        } catch {
            Write-Host "  Error creating mappings for $($instance.name): $_" -ForegroundColor Yellow
        }
    }
}

# 7. 创建操作模式
Write-Host "`n=== 7. 创建操作模式 ===" -ForegroundColor Cyan

$operationModes = @(
    @{
        name = "正常运行"
        description = "电机正常工作状态"
        triggerTagId = "Motor1_Speed"
        triggerMinValue = 30
        triggerMaxValue = 60
        minDurationMs = 5000
        maxDurationMs = 0
        priority = 1
    },
    @{
        name = "低速运行"
        description = "电机低速运转状态"
        triggerTagId = "Motor1_Speed"
        triggerMinValue = 10
        triggerMaxValue = 30
        minDurationMs = 3000
        maxDurationMs = 0
        priority = 2
    },
    @{
        name = "空载待机"
        description = "电机空载或待机状态"
        triggerTagId = "Motor1_Speed"
        triggerMinValue = 0
        triggerMaxValue = 10
        minDurationMs = 2000
        maxDurationMs = 0
        priority = 3
    }
)

foreach ($instance in $createdInstances) {
    foreach ($mode in $operationModes) {
        try {
            $result = Invoke-RestMethod -Uri "$ApiBase/motor-instances/$($instance.instanceId)/modes" -Method Post -Headers $headers -Body ($mode | ConvertTo-Json)
            Write-Host "  Created mode '$($result.name)' for $($instance.name)" -ForegroundColor Green
        } catch {
            Write-Host "  Error creating mode $($mode.name): $_" -ForegroundColor Yellow
        }
    }
}

# 8. 启动基线学习
Write-Host "`n=== 8. 启动基线学习 ===" -ForegroundColor Cyan

foreach ($instance in $createdInstances) {
    try {
        # 使用最近 1 小时的数据进行学习
        $endTs = [DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds()
        $startTs = $endTs - (60 * 60 * 1000)  # 1 hour ago

        $learnBody = @{
            startTs = $startTs
            endTs = $endTs
        } | ConvertTo-Json

        $result = Invoke-RestMethod -Uri "$ApiBase/motor-instances/$($instance.instanceId)/learn-all" -Method Post -Headers $headers -Body $learnBody
        Write-Host "  Started learning for $($instance.name): Task $($result.taskId)" -ForegroundColor Green
    } catch {
        Write-Host "  Error starting learning for $($instance.name): $_" -ForegroundColor Yellow
    }
}

# 9. 验证结果
Write-Host "`n=== 9. 验证结果 ===" -ForegroundColor Cyan

Start-Sleep -Seconds 3  # 等待学习任务处理

$instances = Invoke-RestMethod -Uri "$ApiBase/motor-instances" -Method Get -Headers $headers
Write-Host "  Total motor instances: $($instances.Count)" -ForegroundColor Green

foreach ($inst in $instances) {
    $detail = Invoke-RestMethod -Uri "$ApiBase/motor-instances/$($inst.instanceId)/detail" -Method Get -Headers $headers
    Write-Host "  - $($inst.name):" -ForegroundColor Cyan
    Write-Host "      Mappings: $($detail.mappings.Count)" -ForegroundColor Gray
    Write-Host "      Modes: $($detail.modes.Count)" -ForegroundColor Gray
    Write-Host "      Baselines: $($detail.baselineCount)" -ForegroundColor Gray
}

Write-Host "`n=== 初始化完成 ===" -ForegroundColor Green
Write-Host "请刷新电机故障预测页面查看结果" -ForegroundColor Yellow
