#!/bin/bash
# 测试波动告警和告警聚合功能
# 使用方法: bash scripts/test-volatility-aggregation.sh

API_BASE="http://localhost:5000/api"

echo "=========================================="
echo "  波动告警 + 告警聚合 功能测试"
echo "=========================================="
echo ""

# 1. 登录获取 Token
echo "=== 1. 登录获取 Token ==="
LOGIN_RESP=$(curl -s -X POST "$API_BASE/auth/login" \
  -H "Content-Type: application/json" \
  -d '{"username":"admin","password":"admin123"}')

TOKEN=$(echo $LOGIN_RESP | python -c "import sys,json; print(json.load(sys.stdin).get('data',{}).get('accessToken',''))" 2>/dev/null)

if [ -z "$TOKEN" ]; then
  echo "登录失败: $LOGIN_RESP"
  exit 1
fi
echo "Token 获取成功 (前50字符): ${TOKEN:0:50}..."
echo ""

AUTH="Authorization: Bearer $TOKEN"

# 2. 查看当前标签
echo "=== 2. 查看可用标签 ==="
curl -s "$API_BASE/tags?pageSize=5" -H "$AUTH" | python -m json.tool 2>/dev/null | head -30
echo ""

# 3. 创建波动告警规则
echo "=== 3. 创建波动告警规则 ==="
echo "规则配置:"
echo "  - TagId: Motor1_Speed (电机转速，波动较大)"
echo "  - ConditionType: volatility"
echo "  - Threshold: 1.0 (标准差超过1.0触发)"
echo "  - RocWindowMs: 10000 (10秒时间窗口)"
echo ""

VOLATILITY_RULE=$(cat <<'EOF'
{
  "ruleId": "test-volatility-001",
  "name": "电机转速波动告警",
  "description": "检测电机转速在10秒内标准差超过1.0",
  "tagId": "Motor1_Speed",
  "conditionType": "volatility",
  "threshold": 1.0,
  "rocWindowMs": 10000,
  "severity": 3,
  "messageTemplate": "[Volatility] {tagId} 波动过大: 标准差={stddev}, 均值={avg}, 阈值={threshold}"
}
EOF
)

echo "发送请求..."
CREATE_RESP=$(curl -s -X POST "$API_BASE/alarm-rules" \
  -H "$AUTH" \
  -H "Content-Type: application/json" \
  -d "$VOLATILITY_RULE")

echo "创建结果:"
echo $CREATE_RESP | python -m json.tool 2>/dev/null
echo ""

# 4. 查看当前告警规则
echo "=== 4. 查看所有告警规则 ==="
curl -s "$API_BASE/alarm-rules" -H "$AUTH" | python -m json.tool 2>/dev/null
echo ""

# 5. 等待告警生成
echo "=== 5. 等待告警生成 (15秒) ==="
echo "波动告警需要累积足够数据才能计算标准差..."
for i in {1..15}; do
  echo -n "."
  sleep 1
done
echo ""
echo ""

# 6. 查看生成的告警
echo "=== 6. 查看生成的告警 ==="
ALARMS=$(curl -s "$API_BASE/alarms?pageSize=10" -H "$AUTH")
echo $ALARMS | python -m json.tool 2>/dev/null
echo ""

# 7. 查看告警聚合组
echo "=== 7. 查看告警聚合组 ==="
GROUPS=$(curl -s "$API_BASE/alarm-groups?pageSize=10" -H "$AUTH" 2>/dev/null)
if [ -n "$GROUPS" ]; then
  echo $GROUPS | python -m json.tool 2>/dev/null
else
  echo "告警聚合组 API 可能不存在，检查 /api/alarm-groups 端点"
fi
echo ""

# 8. 检查 Edge 服务日志中的波动告警
echo "=== 8. 检查服务日志 ==="
echo "查看最近的波动告警日志 (如果 Edge 服务在运行)..."
echo ""

# 9. 清理测试规则 (可选)
echo "=== 9. 清理测试规则 ==="
read -p "是否删除测试规则? (y/N): " -n 1 -r
echo ""
if [[ $REPLY =~ ^[Yy]$ ]]; then
  curl -s -X DELETE "$API_BASE/alarm-rules/test-volatility-001" -H "$AUTH" | python -m json.tool 2>/dev/null
  echo "测试规则已删除"
else
  echo "保留测试规则"
fi

echo ""
echo "=========================================="
echo "  测试完成"
echo "=========================================="
