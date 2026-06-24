#!/usr/bin/env bash
# Iniciado pelo plugin PgTranslateLive ou manualmente.
# ROOT substituido em make install-plugin.

ROOT="__PIPELINE_ROOT__"
PORT="${PG_TRANSLATE_PORT:-8765}"
HOST="${PG_TRANSLATE_HOST:-127.0.0.1}"
PIDFILE="$ROOT/cache/serve.pid"
LOG="$ROOT/cache/serve.log"

mkdir -p "$ROOT/cache"

if [[ -f "$PIDFILE" ]]; then
  old_pid="$(cat "$PIDFILE" 2>/dev/null || true)"
  if [[ -n "$old_pid" ]] && kill -0 "$old_pid" 2>/dev/null; then
    exit 0
  fi
fi

cd "$ROOT" || exit 1
nohup nice -n 10 ionice -c3 python3 -m src serve --host "$HOST" --port "$PORT" >>"$LOG" 2>&1 &
echo "serve iniciado (porta $PORT, log $LOG)"
