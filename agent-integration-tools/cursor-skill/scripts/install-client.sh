#!/bin/bash

set -e

# 获取脚本所在目录
SCRIPT_DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )"
CONFIG_PATH="${1:-$SCRIPT_DIR/../config.json}"

if [ ! -f "$CONFIG_PATH" ]; then
    echo "Error: Config file not found: $CONFIG_PATH"
    echo "Please fill config.json before running."
    exit 1
fi

# 检查 jq 是否安装
if ! command -v jq &> /dev/null; then
    echo "Error: 'jq' is required but not installed."
    echo "Please install it using: brew install jq (macOS) or apt-get install jq (Linux)"
    exit 1
fi

# 读取配置
SERVER_IP=$(jq -r '.client.serverIp // empty' "$CONFIG_PATH")
PORT=$(jq -r '.client.port // empty' "$CONFIG_PATH")
SERVER_NAME=$(jq -r '.client.serverName // empty' "$CONFIG_PATH")
HOOK_ENABLED=$(jq -r '.client.hook.enabled // empty' "$CONFIG_PATH")
HOOK_REPLACE=$(jq -r '.client.hook.replaceExisting // empty' "$CONFIG_PATH")
RULE_FILE=$(jq -r '.client.hook.ruleFileName // empty' "$CONFIG_PATH")
AGENT_FILE=$(jq -r '.client.hook.agentFileName // empty' "$CONFIG_PATH")

# 检查必填项
MISSING_FIELDS=()
[ -z "$SERVER_IP" ] && MISSING_FIELDS+=("client.serverIp")
[ -z "$PORT" ] && MISSING_FIELDS+=("client.port")
[ -z "$SERVER_NAME" ] && MISSING_FIELDS+=("client.serverName")
[ -z "$HOOK_ENABLED" ] && MISSING_FIELDS+=("client.hook.enabled")
[ -z "$HOOK_REPLACE" ] && MISSING_FIELDS+=("client.hook.replaceExisting")
[ -z "$RULE_FILE" ] && MISSING_FIELDS+=("client.hook.ruleFileName")
[ -z "$AGENT_FILE" ] && MISSING_FIELDS+=("client.hook.agentFileName")

if [ ${#MISSING_FIELDS[@]} -ne 0 ]; then
    echo "Error: config.json has empty required values. Fill these fields first: ${MISSING_FIELDS[*]}"
    exit 1
fi

WORKSPACE=$(pwd)
CURSOR_DIR="$WORKSPACE/.cursor"
RULES_DIR="$CURSOR_DIR/rules"
AGENTS_DIR="$CURSOR_DIR/agents"
MCP_FILE="$CURSOR_DIR/mcp.json"
ENDPOINT="http://${SERVER_IP}:${PORT}/mcp"

echo "Workspace : $WORKSPACE"
echo "Config    : $CONFIG_PATH"
echo "MCP Name  : $SERVER_NAME"
echo "MCP URL   : $ENDPOINT"
echo "Hook      : $HOOK_ENABLED"

# 创建目录
mkdir -p "$CURSOR_DIR"
mkdir -p "$RULES_DIR"
mkdir -p "$AGENTS_DIR"

# 备份函数
backup_file() {
    local file=$1
    if [ -f "$file" ]; then
        local backup_path="${file}.$(date +%Y%m%d%H%M%S).bak"
        cp "$file" "$backup_path"
        echo "Backup    : $backup_path"
    fi
}

backup_file "$MCP_FILE"

# 更新 mcp.json
if [ -f "$MCP_FILE" ]; then
    # 尝试合并现有配置
    if jq -e . "$MCP_FILE" >/dev/null 2>&1; then
        jq --arg name "$SERVER_NAME" --arg url "$ENDPOINT" \
           '.mcpServers[$name] = {"command": "curl", "args": ["-s", "-X", "POST", "-H", "Content-Type: application/json", "-d", "{}", $url]}' \
           "$MCP_FILE" > "${MCP_FILE}.tmp" && mv "${MCP_FILE}.tmp" "$MCP_FILE"
    else
        echo "Warning: Existing mcp.json parse failed, fallback to overwrite."
        jq -n --arg name "$SERVER_NAME" --arg url "$ENDPOINT" \
           '{"mcpServers": {($name): {"command": "curl", "args": ["-s", "-X", "POST", "-H", "Content-Type: application/json", "-d", "{}", $url]}}}' > "$MCP_FILE"
    fi
else
    jq -n --arg name "$SERVER_NAME" --arg url "$ENDPOINT" \
       '{"mcpServers": {($name): {"command": "curl", "args": ["-s", "-X", "POST", "-H", "Content-Type: application/json", "-d", "{}", $url]}}}' > "$MCP_FILE"
fi

echo "Updated   : $MCP_FILE"

# 处理 Hooks
if [ "$HOOK_ENABLED" = "true" ]; then
    TEMPLATE_RULES_DIR="$SCRIPT_DIR/../templates/rules"
    TEMPLATE_AGENTS_DIR="$SCRIPT_DIR/../templates/agents"
    
    write_managed_file() {
        local src=$1
        local dest=$2
        
        if [ -f "$dest" ] && [ "$HOOK_REPLACE" = "false" ]; then
            echo "Skip      : $dest (exists and replaceExisting=false)"
            return
        fi
        
        backup_file "$dest"
        # 替换占位符并写入
        sed "s|{{MCP_ENDPOINT}}|$ENDPOINT|g" "$src" > "$dest"
        echo "Updated   : $dest"
    }

    # 处理主规则文件
    if [ -f "$TEMPLATE_RULES_DIR/$RULE_FILE" ]; then
        write_managed_file "$TEMPLATE_RULES_DIR/$RULE_FILE" "$RULES_DIR/$RULE_FILE"
    fi

    # 处理主 Agent 文件
    if [ -f "$TEMPLATE_AGENTS_DIR/$AGENT_FILE" ]; then
        write_managed_file "$TEMPLATE_AGENTS_DIR/$AGENT_FILE" "$AGENTS_DIR/$AGENT_FILE"
    fi

    # 复制其他规则文件
    if [ -d "$TEMPLATE_RULES_DIR" ]; then
        for f in "$TEMPLATE_RULES_DIR"/*.mdc; do
            filename=$(basename "$f")
            if [ "$filename" != "$RULE_FILE" ] && [ "$filename" != "*.mdc" ]; then
                write_managed_file "$f" "$RULES_DIR/$filename"
            fi
        done
    fi

    # 复制其他 Agent 文件
    if [ -d "$TEMPLATE_AGENTS_DIR" ]; then
        for f in "$TEMPLATE_AGENTS_DIR"/*.md; do
            filename=$(basename "$f")
            if [ "$filename" != "$AGENT_FILE" ] && [ "$filename" != "*.md" ]; then
                write_managed_file "$f" "$AGENTS_DIR/$filename"
            fi
        done
    fi
fi

echo ""
echo "Done. Cursor MCP and hook config updated:"
echo "  $MCP_FILE"
if [ "$HOOK_ENABLED" = "true" ]; then
    echo "  $RULES_DIR/$RULE_FILE"
    echo "  $AGENTS_DIR/$AGENT_FILE"
fi
echo ""
echo "Next steps:"
echo "1) Restart Cursor"
echo "2) Check MCP panel: '$SERVER_NAME' should be connected"
echo "3) Optional verify: $ENDPOINT"
