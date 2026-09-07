#!/usr/bin/env bash
# Builds the distributable Windows binary and checks that it starts.
#
#   bash tools/export.sh [output-dir]        (default: build/windows)
#
# Requires Godot's export templates. They are ~1.2GB and are NOT vendored with
# this repository; install them once with:
#
#   curl -sL -o templates.tpz \
#     https://github.com/godotengine/godot/releases/download/4.7.2-stable/Godot_v4.7.2-stable_mono_export_templates.tpz
#   unzip -q templates.tpz -d tpl
#   mkdir -p "$APPDATA/Godot/export_templates/4.7.2.stable.mono"
#   cp -r tpl/templates/. "$APPDATA/Godot/export_templates/4.7.2.stable.mono/"
#
# Two things this script exists to stop happening again:
#
#   1. The export needs LostCrownlike.sln next to the .csproj. The project was
#      always built with `dotnet build` on the csproj directly, so no solution
#      existed, and the export failed with "no solution file was found" -- but
#      it still WROTE an exe and a pck. That binary launched, showed nothing,
#      and crashed with signal 11 in the debug build: the .NET assemblies were
#      never published. An export that half-succeeds is worse than one that
#      fails, so this checks for the assemblies afterwards rather than trusting
#      the exit code.
#
#   2. The export path's directory must already exist. Godot reports the
#      missing directory as "the specified export path does not exist", which
#      reads as a bad path rather than a missing folder.
set -uo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
GODOT="$ROOT/tools/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe"
GAME="$ROOT/game"
OUT="${1:-$ROOT/build/windows}"
NAME="PrincessWarrior"
TEMPLATES="${APPDATA:-$HOME/.local/share}/Godot/export_templates/4.7.2.stable.mono"

fail() { printf '\033[31m%s\033[0m\n' "$1"; exit 1; }
ok()   { printf '\033[32m%s\033[0m\n' "$1"; }

[ -f "$TEMPLATES/windows_release_x86_64.exe" ] \
  || fail "no export templates at $TEMPLATES - see the header of this script"

[ -f "$GAME/LostCrownlike.sln" ] \
  || fail "no LostCrownlike.sln - the .NET export needs one next to the .csproj"

mkdir -p "$OUT"
rm -f "$OUT/$NAME.exe" "$OUT/$NAME.pck"

printf 'Exporting to %s\n' "$OUT"
LOG="$(timeout 900 "$GODOT" --headless --path "$GAME" \
        --export-release "Windows Desktop" "$OUT/$NAME.exe" 2>&1)"

if grep -qE "^ERROR" <<<"$LOG"; then
  printf '%s\n' "$LOG" | grep -E "^ERROR" | head -5
  fail "export reported errors"
fi

[ -f "$OUT/$NAME.exe" ] || fail "no executable was produced"

# The assemblies, not just the exe. This is the half that was silently missing.
DATA="$OUT/data_LostCrownlike_windows_x86_64"
[ -f "$DATA/LostCrownlike.dll" ] \
  || fail "no LostCrownlike.dll in $DATA - the .NET project was not published"

# And it has to start. "AudioManager: ready" comes from a C# autoload, so its
# presence is proof the managed runtime came up, not just the engine.
# Launched, watched, then KILLED BY PID -- not wrapped in `timeout`.
#
# `timeout` sends a POSIX signal, and a native Windows binary launched from Git
# Bash does not answer one: on a run where the game did not quit itself, the
# gate sat on this line for SEVEN MINUTES with the process at 0.00 seconds of
# CPU, and only a manual taskkill freed it. A gate with no upper bound on its
# own runtime is a gate people stop running.
#
# --quit-after is still passed, so the normal path is the game leaving on its
# own after 240 frames; this is the backstop for when it does not.
BOOT_LOG="$(mktemp)"
( cd "$OUT" && "./$NAME.exe" --quit-after 240 >"$BOOT_LOG" 2>&1 ) &
BOOT_PID=$!
for _ in $(seq 1 60); do
  kill -0 "$BOOT_PID" 2>/dev/null || break
  grep -q "AudioManager: ready" "$BOOT_LOG" 2>/dev/null && break
  sleep 1
done
# taskkill, not kill: the shell job is a wrapper, and the tree under it is what
# holds the window open. //T //F is the Git Bash escaping for /T /F.
if kill -0 "$BOOT_PID" 2>/dev/null; then
  CHILD="$(powershell.exe -NoProfile -Command "(Get-Process -Name '$NAME' -ErrorAction SilentlyContinue).Id" 2>/dev/null | tr -d '')"
  for pid in $CHILD; do taskkill //PID "$pid" //T //F >/dev/null 2>&1; done
  kill "$BOOT_PID" 2>/dev/null
fi
wait "$BOOT_PID" 2>/dev/null
BOOT="$(cat "$BOOT_LOG")"
rm -f "$BOOT_LOG"
if grep -qE "^ERROR|^SCRIPT ERROR|CrashHandlerException" <<<"$BOOT"; then
  printf '%s\n' "$BOOT" | grep -E "^ERROR|^SCRIPT ERROR|CrashHandlerException" | head -5
  fail "the exported game printed errors on startup"
fi
grep -q "AudioManager: ready" <<<"$BOOT" \
  || fail "the exported game started but its C# autoloads did not run"

SIZE="$(du -sh "$OUT" | cut -f1)"
ok "Exported and started clean: $OUT ($SIZE)"
