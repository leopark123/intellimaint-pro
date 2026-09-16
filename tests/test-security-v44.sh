#!/bin/bash
#
# IntelliMaint Pro v44 安全功能测试脚本
# 用法: ./test-security-v44.sh [BASE_URL]
#

BASE_URL="${1:-http://localhost:5000}"
USERNAME="${ADMIN_USERNAME:?Set ADMIN_USERNAME}"
PASSWORD="${ADMIN_PASSWORD:?Set ADMIN_PASSWORD}"

# 颜色
GREEN='\033[0;32m'
RED='\033[0;31m'
BLUE='\033[0;34m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

PASS_COUNT=0
FAIL_COUNT=0

pass() {
    echo -e "${GREEN}✅ $1${NC}"
    ((PASS_COUNT++))
}

fail() {
    echo -e "${RED}❌ $1${NC}"
    ((FAIL_COUNT++))
}

info() {
    echo -e "${BLUE}ℹ️  $1${NC}"
}

header() {
    echo ""
    echo -e "${YELLOW}═══════════════════════════════════════════════════════════${NC}"
    echo -e "${YELLOW}  $1${NC}"
    echo -e "${YELLOW}═══════════════════════════════════════════════════════════${NC}"
}

# 检查依赖
check_deps() {
    if ! command -v curl &> /dev/null; then
        echo "错误: 需要安装 curl"
        exit 1
    fi
    if ! command -v jq &> /dev/null; then
        echo "警告: jq 未安装，部分功能受限"
        echo "安装: sudo apt install jq (Ubuntu) 或 brew install jq (Mac)"
    fi
}

# ============================================
# 测试 1: 基础连接
# ============================================
test_connection() {
    header "测试 1: 基础连接"
    
    RESPONSE=$(curl -s -o /dev/null -w "%{http_code}" "$BASE_URL" 2>/dev/null)
    
    if [ "$RESPONSE" = "200" ]; then
        pass "API 服务可访问"
    else
        fail "API 服务不可访问 (HTTP $RESPONSE)"
        echo "请确保后端服务已启动: dotnet run --project src/Host.Api"
        exit 1
    fi
}

# ============================================
# 测试 2: JWT 认证
# ============================================
test_jwt() {
    header "测试 2: JWT 认证"
    
    # 2.1 登录成功
    info "测试登录..."
    LOGIN_RESPONSE=$(curl -s -X POST "$BASE_URL/api/auth/login" \
        -H "Content-Type: application/json" \
        -d "{\"username\":\"$USERNAME\",\"password\":\"$PASSWORD\"}")
    
    if command -v jq &> /dev/null; then
        TOKEN=$(echo "$LOGIN_RESPONSE" | jq -r '.data.token // empty')
        REFRESH_TOKEN=$(echo "$LOGIN_RESPONSE" | jq -r '.data.refreshToken // empty')
        ROLE=$(echo "$LOGIN_RESPONSE" | jq -r '.data.role // empty')
    else
        # 简单提取 (不推荐)
        TOKEN=$(echo "$LOGIN_RESPONSE" | grep -o '"token":"[^"]*"' | cut -d'"' -f4)
        REFRESH_TOKEN=$(echo "$LOGIN_RESPONSE" | grep -o '"refreshToken":"[^"]*"' | cut -d'"' -f4)
    fi
    
    if [ -n "$TOKEN" ]; then
        pass "登录成功，获取 Token"
    else
        fail "登录失败"
        echo "认证响应未输出，以免泄露凭据。"
        exit 1
    fi
    
    if [ -n "$REFRESH_TOKEN" ]; then
        pass "获取 Refresh Token"
    else
        fail "未获取 Refresh Token"
    fi
    
    # 2.2 登录失败
    info "测试错误密码..."
    FAIL_RESPONSE=$(curl -s -o /dev/null -w "%{http_code}" -X POST "$BASE_URL/api/auth/login" \
        -H "Content-Type: application/json" \
        -d "{\"username\":\"$USERNAME\",\"password\":\"wrongpassword\"}")
    
    if [ "$FAIL_RESPONSE" = "401" ]; then
        pass "错误密码返回 401"
    else
        fail "预期 401，实际 $FAIL_RESPONSE"
    fi
    
    # 2.3 无 Token 访问
    info "测试无 Token 访问..."
    NO_AUTH_RESPONSE=$(curl -s -o /dev/null -w "%{http_code}" "$BASE_URL/api/devices")
    
    if [ "$NO_AUTH_RESPONSE" = "401" ]; then
        pass "无 Token 返回 401"
    else
        fail "预期 401，实际 $NO_AUTH_RESPONSE"
    fi
    
    # 2.4 有 Token 访问
    info "测试有 Token 访问..."
    AUTH_RESPONSE=$(curl -s -o /dev/null -w "%{http_code}" "$BASE_URL/api/devices" \
        -H "Authorization: Bearer $TOKEN")
    
    if [ "$AUTH_RESPONSE" = "200" ]; then
        pass "有 Token 返回 200"
    else
        fail "预期 200，实际 $AUTH_RESPONSE"
    fi
    
    # 2.5 Token 刷新
    info "测试 Token 刷新..."
    REFRESH_RESPONSE=$(curl -s -X POST "$BASE_URL/api/auth/refresh" \
        -H "Content-Type: application/json" \
        -d "{\"refreshToken\":\"$REFRESH_TOKEN\"}")
    
    if command -v jq &> /dev/null; then
        NEW_TOKEN=$(echo "$REFRESH_RESPONSE" | jq -r '.data.token // empty')
    else
        NEW_TOKEN=$(echo "$REFRESH_RESPONSE" | grep -o '"token":"[^"]*"' | cut -d'"' -f4)
    fi
    
    if [ -n "$NEW_TOKEN" ] && [ "$NEW_TOKEN" != "$TOKEN" ]; then
        pass "Token 刷新成功 (新 Token 不同)"
        TOKEN="$NEW_TOKEN"
    else
        fail "Token 刷新失败或 Token 未变化"
    fi
}

# ============================================
# 测试 3: SignalR 授权
# ============================================
test_signalr() {
    header "测试 3: SignalR 授权"
    
    # 3.1 无 Token 连接
    info "测试 SignalR 无 Token 连接..."
    SIGNALR_NO_AUTH=$(curl -s -o /dev/null -w "%{http_code}" -X POST \
        "$BASE_URL/hubs/telemetry/negotiate?negotiateVersion=1")
    
    if [ "$SIGNALR_NO_AUTH" = "401" ]; then
        pass "SignalR 无 Token 返回 401"
    else
        fail "预期 401，实际 $SIGNALR_NO_AUTH"
    fi
    
    # 3.2 有 Token 连接
    info "测试 SignalR 有 Token 连接..."
    SIGNALR_AUTH=$(curl -s -o /dev/null -w "%{http_code}" -X POST \
        "$BASE_URL/hubs/telemetry/negotiate?negotiateVersion=1" \
        -H "Authorization: Bearer $TOKEN")
    
    if [ "$SIGNALR_AUTH" = "200" ]; then
        pass "SignalR 有 Token 返回 200"
    else
        fail "预期 200，实际 $SIGNALR_AUTH"
    fi
    
    # 3.3 Query String Token
    info "测试 SignalR Query String Token..."
    SIGNALR_QS=$(curl -s -o /dev/null -w "%{http_code}" -X POST \
        "$BASE_URL/hubs/telemetry/negotiate?negotiateVersion=1&access_token=$TOKEN")
    
    if [ "$SIGNALR_QS" = "200" ]; then
        pass "SignalR Query String Token 有效"
    else
        fail "预期 200，实际 $SIGNALR_QS"
    fi
}

# ============================================
# 测试 4: 请求限流
# ============================================
test_rate_limit() {
    header "测试 4: 请求限流"
    
    info "发送 110 次请求测试限流..."
    
    SUCCESS_COUNT=0
    LIMITED_COUNT=0
    
    for i in $(seq 1 110); do
        RESPONSE=$(curl -s -o /dev/null -w "%{http_code}" "$BASE_URL/api/devices" \
            -H "Authorization: Bearer $TOKEN")
        
        if [ "$RESPONSE" = "200" ]; then
            ((SUCCESS_COUNT++))
        elif [ "$RESPONSE" = "429" ]; then
            ((LIMITED_COUNT++))
        fi
        
        # 显示进度
        if [ $((i % 20)) -eq 0 ]; then
            echo "  进度: $i/110 (成功: $SUCCESS_COUNT, 限流: $LIMITED_COUNT)"
        fi
    done
    
    info "结果: 成功=$SUCCESS_COUNT, 被限流=$LIMITED_COUNT"
    
    if [ $SUCCESS_COUNT -ge 95 ] && [ $SUCCESS_COUNT -le 105 ]; then
        pass "成功请求数约 100 (实际: $SUCCESS_COUNT)"
    else
        fail "成功请求数异常 (实际: $SUCCESS_COUNT)"
    fi
    
    if [ $LIMITED_COUNT -ge 5 ]; then
        pass "被限流请求数 >= 5 (实际: $LIMITED_COUNT)"
    else
        fail "限流未生效 (被限流: $LIMITED_COUNT)"
    fi
}

# ============================================
# 测试 5: 审计日志
# ============================================
test_audit() {
    header "测试 5: 审计日志"
    
    # 等待限流窗口恢复
    info "等待 5 秒..."
    sleep 5
    
    # 重新登录获取新 Token
    LOGIN_RESPONSE=$(curl -s -X POST "$BASE_URL/api/auth/login" \
        -H "Content-Type: application/json" \
        -d "{\"username\":\"$USERNAME\",\"password\":\"$PASSWORD\"}")
    
    if command -v jq &> /dev/null; then
        TOKEN=$(echo "$LOGIN_RESPONSE" | jq -r '.data.token // empty')
    else
        TOKEN=$(echo "$LOGIN_RESPONSE" | grep -o '"token":"[^"]*"' | cut -d'"' -f4)
    fi
    
    info "查询审计日志..."
    AUDIT_RESPONSE=$(curl -s "$BASE_URL/api/audit?pageSize=10" \
        -H "Authorization: Bearer $TOKEN")
    
    if command -v jq &> /dev/null; then
        SUCCESS=$(echo "$AUDIT_RESPONSE" | jq -r '.success // false')
        ITEMS_COUNT=$(echo "$AUDIT_RESPONSE" | jq '.data.items | length // 0')
        HAS_LOGIN=$(echo "$AUDIT_RESPONSE" | jq '[.data.items[] | select(.action == "Login")] | length > 0')
        HAS_IP=$(echo "$AUDIT_RESPONSE" | jq '[.data.items[] | select(.ipAddress != null and .ipAddress != "")] | length > 0')
        
        if [ "$SUCCESS" = "true" ]; then
            pass "审计日志查询成功"
        else
            fail "审计日志查询失败"
        fi
        
        if [ "$ITEMS_COUNT" -gt 0 ]; then
            pass "审计日志有记录 (数量: $ITEMS_COUNT)"
        else
            fail "审计日志无记录"
        fi
        
        if [ "$HAS_LOGIN" = "true" ]; then
            pass "包含登录审计记录"
        else
            fail "未找到登录审计记录"
        fi
        
        if [ "$HAS_IP" = "true" ]; then
            pass "审计记录包含 IP 地址"
        else
            fail "审计记录无 IP 地址"
        fi
        
        # 显示最近记录
        info "最近 3 条审计记录:"
        echo "$AUDIT_RESPONSE" | jq -r '.data.items[:3][] | "  [\(.ts | . / 1000 | strftime("%H:%M:%S"))] \(.userName) - \(.action) - IP: \(.ipAddress // "N/A")"'
    else
        # 简单检查
        if echo "$AUDIT_RESPONSE" | grep -q '"success":true'; then
            pass "审计日志查询成功"
        else
            fail "审计日志查询失败"
        fi
    fi
}

# ============================================
# 主流程
# ============================================
main() {
    echo ""
    echo "╔══════════════════════════════════════════════════════════╗"
    echo "║     IntelliMaint Pro v44 安全功能自动化测试              ║"
    echo "╚══════════════════════════════════════════════════════════╝"
    echo ""
    info "目标: $BASE_URL"
    
    check_deps
    test_connection
    test_jwt
    test_signalr
    test_rate_limit
    test_audit
    
    header "测试总结"
    
    TOTAL=$((PASS_COUNT + FAIL_COUNT))
    PASS_RATE=0
    if [ $TOTAL -gt 0 ]; then
        PASS_RATE=$((PASS_COUNT * 100 / TOTAL))
    fi
    
    echo ""
    echo "  总测试数: $TOTAL"
    pass "通过: $PASS_COUNT"
    if [ $FAIL_COUNT -gt 0 ]; then
        fail "失败: $FAIL_COUNT"
    fi
    echo "  通过率: $PASS_RATE%"
    echo ""
    
    if [ $FAIL_COUNT -eq 0 ]; then
        echo -e "${GREEN}🎉 所有测试通过！v44 安全功能正常工作。${NC}"
    else
        echo -e "${YELLOW}⚠️  部分测试失败，请检查上述错误。${NC}"
    fi
    
    exit $FAIL_COUNT
}

main
