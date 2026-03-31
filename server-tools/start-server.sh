#!/bin/bash
set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
CONFIG="$SCRIPT_DIR/server-config.json"

if [ ! -f "$CONFIG" ]; then
    echo "[ERROR] Config not found: $CONFIG"
    echo "Please copy server-config.example.json to server-config.json and edit it."
    exit 1
fi

if command -v jq &>/dev/null; then
    APP_PATH_RAW=$(jq -r '.appPath' "$CONFIG")
    DB_PATH=$(jq -r '.dbPath' "$CONFIG")
    PORT=$(jq -r '.port // 5051' "$CONFIG")
elif command -v python3 &>/dev/null; then
    APP_PATH_RAW=$(python3 -c "import json; c=json.load(open('$CONFIG')); print(c['appPath'])")
    DB_PATH=$(python3 -c "import json; c=json.load(open('$CONFIG')); print(c['dbPath'])")
    PORT=$(python3 -c "import json; c=json.load(open('$CONFIG')); print(c.get('port', 5051))")
else
    echo "[ERROR] jq or python3 is required to parse config"
    exit 1
fi

APP_IS_COMMAND=0
if [[ "$APP_PATH_RAW" = /* ]]; then
    APP_PATH="$APP_PATH_RAW"
elif [[ "$APP_PATH_RAW" == */* ]]; then
    APP_PATH="$SCRIPT_DIR/$APP_PATH_RAW"
else
    APP_PATH="$APP_PATH_RAW"
    APP_IS_COMMAND=1
fi

if [[ "$DB_PATH" != /* ]]; then
    DB_PATH="$(pwd)/$DB_PATH"
fi

if [ "$APP_IS_COMMAND" -eq 1 ]; then
    if ! command -v "$APP_PATH" &>/dev/null; then
        echo "[ERROR] Executable command not found in PATH: $APP_PATH"
        echo "Please set appPath in server-config.json."
        exit 1
    fi
    APP_DISPLAY="$(command -v "$APP_PATH")"
else
    if [ ! -x "$APP_PATH" ]; then
        if [ -f "$APP_PATH" ]; then
            echo "[ERROR] File exists but is not executable: $APP_PATH"
            echo "Try: chmod +x \"$APP_PATH\""
        else
            echo "[ERROR] Executable not found: $APP_PATH"
        fi
        echo "Please set appPath in server-config.json."
        exit 1
    fi
    APP_DISPLAY="$APP_PATH"
fi

mkdir -p "$DB_PATH"

echo "Starting DNA Server..."
echo "  Executable : $APP_DISPLAY"
echo "  Database   : $DB_PATH"
echo "  Port       : $PORT"
echo ""

"$APP_PATH" --db "$DB_PATH" --port "$PORT"
