#!/usr/bin/env bash
# Wrapper Steam: sobe daemon CDN em background + abre o jogo + para daemon ao sair.
# Launch Options:
#   WINEDLLOVERRIDES="winhttp=n,b" /caminho/translations/scripts/pg-launch.sh %command%

set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PIDFILE="$ROOT/cache/daemon.pid"
LOG="$ROOT/cache/daemon.log"
PYTHON="${PYTHON:-python3}"

DAEMON_BATCH="${DAEMON_BATCH:-150}"
DAEMON_WORKERS="${DAEMON_WORKERS:-4}"
DAEMON_PAUSE="${DAEMON_PAUSE:-120}"
DAEMON_INSTALL_INTERVAL="${DAEMON_INSTALL_INTERVAL:-600}"
DAEMON_CATEGORIES="${DAEMON_CATEGORIES:-skills,abilities,ui,items,quests,npcs,effects}"

start_daemon() {
  if [[ -f "$PIDFILE" ]]; then
    local pid
    pid="$(cat "$PIDFILE")"
    if kill -0 "$pid" 2>/dev/null; then
      echo "[pg-launch] daemon já ativo (pid $pid)" | tee -a "$LOG"
      return
    fi
    rm -f "$PIDFILE"
  fi

  echo "[pg-launch] iniciando daemon CDN..." | tee -a "$LOG"
  (
    cd "$ROOT"
    exec nice -n 19 ionice -c3 "$PYTHON" -m src translate-daemon \
      --batch-size "$DAEMON_BATCH" \
      --workers "$DAEMON_WORKERS" \
      --pause "$DAEMON_PAUSE" \
      --install-interval "$DAEMON_INSTALL_INTERVAL" \
      --categories "$DAEMON_CATEGORIES" \
      --install-on-sync
  ) >>"$LOG" 2>&1 &
  for _ in $(seq 1 30); do
    [[ -f "$PIDFILE" ]] && break
    sleep 0.1
  done
}

stop_daemon() {
  if [[ ! -f "$PIDFILE" ]]; then
    return
  fi
  local pid
  pid="$(cat "$PIDFILE")"
  if kill -0 "$pid" 2>/dev/null; then
    kill "$pid" 2>/dev/null || true
    for _ in $(seq 1 30); do
      kill -0 "$pid" 2>/dev/null || break
      sleep 0.2
    done
    kill -9 "$pid" 2>/dev/null || true
  fi
  rm -f "$PIDFILE"
  echo "[pg-launch] daemon parado" >>"$LOG"
}

trap stop_daemon EXIT INT TERM

if [[ $# -eq 0 ]]; then
  echo "Uso: pg-launch.sh <comando do jogo...>" >&2
  echo "Steam Launch Options:" >&2
  echo "  WINEDLLOVERRIDES=\"winhttp=n,b\" $ROOT/scripts/pg-launch.sh %command%" >&2
  exit 1
fi

start_daemon
exec "$@"
