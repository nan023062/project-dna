#!/bin/bash

set -e

SCRIPT_DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )"
CONFIG_PATH="${1:-$SCRIPT_DIR/../server-config.json}"

if [ ! -f "$CONFIG_PATH" ]; then
    echo "Error: Config file not found: $CONFIG_PATH"
    echo "Please fill config.json before running."
    exit 1
fi

if ! command -v jq &> /dev/null; then
    echo "Error: 'jq' is required but not installed."
    exit 1
fi

MODE=$(jq -r '.server.mode // "binary"' "$CONFIG_PATH")
APP_PATH=$(jq -r '.server.appPath // empty' "$CONFIG_PATH")
DB_PATH=$(jq -r '.server.dbPath // empty' "$CONFIG_PATH")
PORT=$(jq -r '.server.port // 5051' "$CONFIG_PATH")

if [ -z "$APP_PATH" ]; then echo "Error: server.appPath is required in config.json"; exit 1; fi
if [ -z "$DB_PATH" ]; then echo "Error: server.dbPath is required in config.json"; exit 1; fi

WORKSPACE=$(pwd)

# 解析绝对路径
if [[ "$DB_PATH" = /* ]]; then
    ABS_DB_PATH="$DB_PATH"
else
    ABS_DB_PATH="$WORKSPACE/$DB_PATH"
fi

mkdir -p "$ABS_DB_PATH"

echo "Starting Project DNA Server..."
echo "Mode    : $MODE"
echo "App Path: $APP_PATH"
echo "DB Path : $ABS_DB_PATH"
echo "Port    : $PORT"
echo "----------------------------------------"

if [ "$MODE" = "source" ]; then
    if [[ "$APP_PATH" != /* ]]; then
        APP_PATH="$WORKSPACE/$APP_PATH"
    fi
    
    if [ ! -d "$APP_PATH" ] && [ ! -f "$APP_PATH" ]; then
        echo "Error: Source project not found at: $APP_PATH"
        exit 1
    fi
    
    echo "Running via dotnet run..."
    dotnet run --project "$APP_PATH" -- --db "$ABS_DB_PATH" --port "$PORT"
else
    # 检查是否为全局命令或相对路径
    if [[ "$APP_PATH" != /* ]] && [ -f "$WORKSPACE/$APP_PATH" ]; then
        APP_PATH="$WORKSPACE/$APP_PATH"
    fi
    
    if ! command -v "$APP_PATH" &> /dev/null && [ ! -x "$APP_PATH" ]; then
        echo "Error: Executable not found or not executable: $APP_PATH"
        exit 1
    fi
    
    echo "Running binary..."
    "$APP_PATH" --db "$ABS_DB_PATH" --port "$PORT"
fi
