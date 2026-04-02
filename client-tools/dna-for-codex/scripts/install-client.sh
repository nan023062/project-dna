#!/bin/bash

set -e

SCRIPT_DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )"
CONFIG_PATH="${1:-$SCRIPT_DIR/../config.json}"

if [ ! -f "$CONFIG_PATH" ]; then
    echo "Error: Config file not found: $CONFIG_PATH"
    echo "Please fill config.json before running."
    exit 1
fi

if ! command -v jq &> /dev/null; then
    echo "Error: 'jq' is required but not installed."
    echo "Please install it using: brew install jq (macOS) or apt-get install jq (Linux)"
    exit 1
fi

SERVER_IP=$(jq -r '.client.serverIp // empty' "$CONFIG_PATH")
PORT=$(jq -r '.client.port // empty' "$CONFIG_PATH")
SERVER_NAME=$(jq -r '.client.serverName // empty' "$CONFIG_PATH")
HOOK_ENABLED=$(jq -r '.client.hook.enabled // empty' "$CONFIG_PATH")
HOOK_REPLACE=$(jq -r '.client.hook.replaceExisting // empty' "$CONFIG_PATH")
PROMPT_FILE=$(jq -r '.client.hook.promptFileName // empty' "$CONFIG_PATH")
AGENT_FILE=$(jq -r '.client.hook.agentFileName // empty' "$CONFIG_PATH")

MISSING_FIELDS=()
[ -z "$SERVER_IP" ] && MISSING_FIELDS+=("client.serverIp")
[ -z "$PORT" ] && MISSING_FIELDS+=("client.port")
[ -z "$SERVER_NAME" ] && MISSING_FIELDS+=("client.serverName")
[ -z "$HOOK_ENABLED" ] && MISSING_FIELDS+=("client.hook.enabled")
[ -z "$HOOK_REPLACE" ] && MISSING_FIELDS+=("client.hook.replaceExisting")
[ -z "$PROMPT_FILE" ] && MISSING_FIELDS+=("client.hook.promptFileName")
[ -z "$AGENT_FILE" ] && MISSING_FIELDS+=("client.hook.agentFileName")

if [ ${#MISSING_FIELDS[@]} -ne 0 ]; then
    echo "Error: config.json has empty required values. Fill these fields first: ${MISSING_FIELDS[*]}"
    exit 1
fi

WORKSPACE=$(pwd)
CODEX_DIR="$WORKSPACE/.codex"
PROMPTS_DIR="$CODEX_DIR/prompts"
AGENTS_DIR="$CODEX_DIR/agents"
MCP_FILE="$CODEX_DIR/mcp.json"
ENDPOINT="http://${SERVER_IP}:${PORT}/mcp"

echo "Workspace : $WORKSPACE"
echo "Config    : $CONFIG_PATH"
echo "MCP Name  : $SERVER_NAME"
echo "MCP URL   : $ENDPOINT"
echo "Hook      : $HOOK_ENABLED"

mkdir -p "$CODEX_DIR"
mkdir -p "$PROMPTS_DIR"
mkdir -p "$AGENTS_DIR"

backup_file() {
    local file=$1
    if [ -f "$file" ]; then
        local backup_path="${file}.$(date +%Y%m%d%H%M%S).bak"
        cp "$file" "$backup_path"
        echo "Backup    : $backup_path"
    fi
}

backup_file "$MCP_FILE"

if [ -f "$MCP_FILE" ]; then
    if jq -e . "$MCP_FILE" >/dev/null 2>&1; then
        jq --arg name "$SERVER_NAME" --arg url "$ENDPOINT" \
           '.mcpServers = (.mcpServers // {}) | .mcpServers[$name] = {"url": $url}' \
           "$MCP_FILE" > "${MCP_FILE}.tmp" && mv "${MCP_FILE}.tmp" "$MCP_FILE"
    else
        echo "Warning: Existing mcp.json parse failed, fallback to overwrite."
        jq -n --arg name "$SERVER_NAME" --arg url "$ENDPOINT" \
           '{"mcpServers": {($name): {"url": $url}}}' > "$MCP_FILE"
    fi
else
    jq -n --arg name "$SERVER_NAME" --arg url "$ENDPOINT" \
       '{"mcpServers": {($name): {"url": $url}}}' > "$MCP_FILE"
fi

echo "Updated   : $MCP_FILE"

if [ "$HOOK_ENABLED" = "true" ]; then
    TEMPLATE_PROMPTS_DIR="$SCRIPT_DIR/../templates/prompts"
    TEMPLATE_AGENTS_DIR="$SCRIPT_DIR/../templates/agents"

    write_managed_file() {
        local src=$1
        local dest=$2

        if [ -f "$dest" ] && [ "$HOOK_REPLACE" = "false" ]; then
            echo "Skip      : $dest (exists and replaceExisting=false)"
            return
        fi

        backup_file "$dest"
        sed "s|{{MCP_ENDPOINT}}|$ENDPOINT|g" "$src" > "$dest"
        echo "Updated   : $dest"
    }

    if [ -f "$TEMPLATE_PROMPTS_DIR/$PROMPT_FILE" ]; then
        write_managed_file "$TEMPLATE_PROMPTS_DIR/$PROMPT_FILE" "$PROMPTS_DIR/$PROMPT_FILE"
    fi

    if [ -f "$TEMPLATE_AGENTS_DIR/$AGENT_FILE" ]; then
        write_managed_file "$TEMPLATE_AGENTS_DIR/$AGENT_FILE" "$AGENTS_DIR/$AGENT_FILE"
    fi

    if [ -d "$TEMPLATE_PROMPTS_DIR" ]; then
        while IFS= read -r -d '' f; do
            filename=$(basename "$f")
            if [ "$filename" != "$PROMPT_FILE" ]; then
                write_managed_file "$f" "$PROMPTS_DIR/$filename"
            fi
        done < <(find "$TEMPLATE_PROMPTS_DIR" -maxdepth 1 -type f -name "*.md" -print0)
    fi

    if [ -d "$TEMPLATE_AGENTS_DIR" ]; then
        while IFS= read -r -d '' f; do
            filename=$(basename "$f")
            if [ "$filename" != "$AGENT_FILE" ]; then
                write_managed_file "$f" "$AGENTS_DIR/$filename"
            fi
        done < <(find "$TEMPLATE_AGENTS_DIR" -maxdepth 1 -type f -name "*.md" -print0)
    fi
fi

echo ""
echo "Done. Codex MCP and hook config updated:"
echo "  $MCP_FILE"
if [ "$HOOK_ENABLED" = "true" ]; then
    echo "  $PROMPTS_DIR/$PROMPT_FILE"
    echo "  $AGENTS_DIR/$AGENT_FILE"
fi
echo ""
echo "Next steps:"
echo "1) Restart Codex session"
echo "2) Check MCP server: '$SERVER_NAME' should be connected"
echo "3) Open Client console: http://${SERVER_IP}:${PORT}"
echo "4) Optional verify MCP: $ENDPOINT"
