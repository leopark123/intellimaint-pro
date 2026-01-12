#!/bin/bash
TOKEN="eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJodHRwOi8vc2NoZW1hcy54bWxzb2FwLm9yZy93cy8yMDA1LzA1L2lkZW50aXR5L2NsYWltcy9uYW1laWRlbnRpZmllciI6ImFkbWluMDAwMDAwMDAwMSIsImh0dHA6Ly9zY2hlbWFzLnhtbHNvYXAub3JnL3dzLzIwMDUvMDUvaWRlbnRpdHkvY2xhaW1zL25hbWUiOiJhZG1pbiIsImh0dHA6Ly9zY2hlbWFzLm1pY3Jvc29mdC5jb20vd3MvMjAwOC8wNi9pZGVudGl0eS9jbGFpbXMvcm9sZSI6IkFkbWluIiwiZGlzcGxheV9uYW1lIjoiQWRtaW5pc3RyYXRvciIsImV4cCI6MTc2NzU3NDQ4NSwiaXNzIjoiSW50ZWxsaU1haW50IiwiYXVkIjoiSW50ZWxsaU1haW50In0.zwLs7k6DdaggxQZNAfyl5RVOSfmTz8F5onEt0M1WIRo"
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
