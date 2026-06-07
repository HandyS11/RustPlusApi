#!/usr/bin/env bash
#
# Step 1 — Fetch/update the Rust dedicated server via SteamCMD (app 258550).
#
# Anonymous login, no game license required. The server's managed assemblies are the
# authoritative source of the Companion contracts (SilentOrbit IProto types in Rust.Data.dll;
# §6, Method A).
#
# SteamCMD is auto-installed (no sudo) from Valve's Linux tarball into ./steamcmd if it is
# not already on PATH. Override with STEAMCMD=/path/to/steamcmd to use a system install.
# Note: the tarball needs 32-bit glibc on some distros (Fedora: sudo dnf install glibc.i686).
#
# Usage:  ./1-fetch-server.sh
# Output: ./rds/  (gitignored) + the resolved build id on stdout.

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
RDS_DIR="${SCRIPT_DIR}/rds"
STEAMCMD_DIR="${SCRIPT_DIR}/steamcmd"
STEAMCMD_TARBALL_URL="https://steamcdn-a.akamaihd.net/client/installer/steamcmd_linux.tar.gz"
APP_ID=258550

# Resolve steamcmd: explicit override → on PATH → local install → fetch the tarball.
ensure_steamcmd() {
    if [[ -n "${STEAMCMD:-}" ]]; then
        echo "${STEAMCMD}"; return
    fi
    if command -v steamcmd >/dev/null 2>&1; then
        command -v steamcmd; return
    fi
    if [[ -x "${STEAMCMD_DIR}/steamcmd.sh" ]]; then
        echo "${STEAMCMD_DIR}/steamcmd.sh"; return
    fi
    echo ">> steamcmd not found — installing (no sudo) into ${STEAMCMD_DIR}" >&2
    mkdir -p "${STEAMCMD_DIR}"
    curl -fsSL "${STEAMCMD_TARBALL_URL}" | tar -xz -C "${STEAMCMD_DIR}"
    if [[ ! -x "${STEAMCMD_DIR}/steamcmd.sh" ]]; then
        echo "ERROR: steamcmd install failed — ${STEAMCMD_DIR}/steamcmd.sh not present." >&2
        exit 1
    fi
    echo "${STEAMCMD_DIR}/steamcmd.sh"
}

STEAMCMD_BIN="$(ensure_steamcmd)"

echo ">> Fetching/updating Rust dedicated server (app ${APP_ID}) into ${RDS_DIR}"
"${STEAMCMD_BIN}" \
    +force_install_dir "${RDS_DIR}" \
    +login anonymous \
    +app_update "${APP_ID}" validate \
    +quit

CONTRACT_DLL="${RDS_DIR}/RustDedicated_Data/Managed/Rust.Data.dll"
if [[ ! -f "${CONTRACT_DLL}" ]]; then
    echo "ERROR: expected contract assembly not found: ${CONTRACT_DLL}" >&2
    echo "       The download may have failed or the layout changed." >&2
    exit 1
fi

# Print the build id so a proto diff can be attributed to a specific Rust update.
MANIFEST="${RDS_DIR}/steamapps/appmanifest_${APP_ID}.acf"
BUILD_ID="unknown"
if [[ -f "${MANIFEST}" ]]; then
    BUILD_ID="$(grep -o '"buildid"[^0-9]*[0-9]*' "${MANIFEST}" | grep -o '[0-9]*' || echo unknown)"
fi

echo ">> OK. Contract assembly: ${CONTRACT_DLL}"
echo ">> Server build id: ${BUILD_ID}"
