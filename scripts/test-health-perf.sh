#!/bin/bash
TOKEN="${ACCESS_TOKEN:?Set ACCESS_TOKEN to a local short-lived token}"
BASE_URL="http://localhost:5000/api/health-assessment"

echo "=== 健康评估 API 性能测试 (10次请求) ==="
echo ""

test_api() {
    local name=$1
    local url=$2
    local total=0
    local min=999
    local max=0

    for i in $(seq 1 10); do
        time=$(curl -s -o /dev/null -w "%{time_total}" "$url" -H "Authorization: Bearer $TOKEN")
        total=$(echo "$total + $time" | bc)
        if (( $(echo "$time < $min" | bc -l) )); then min=$time; fi
        if (( $(echo "$time > $max" | bc -l) )); then max=$time; fi
    done

    avg=$(echo "scale=3; $total / 10" | bc)
    printf "%-30s 平均: %ss, 最小: %ss, 最大: %ss\n" "$name" "$avg" "$min" "$max"
}

test_api "所有设备健康评分" "$BASE_URL/devices"
test_api "单设备健康评分" "$BASE_URL/devices/SIM-PLC-001"
test_api "健康汇总统计" "$BASE_URL/summary"
test_api "设备健康历史" "$BASE_URL/devices/SIM-PLC-001/history"

echo ""
echo "=== 测试完成 ==="
