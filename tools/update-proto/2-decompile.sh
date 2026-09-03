#!/usr/bin/env bash
#
# Step 2 — Decompile the Companion contract types with ilspycmd.
#
# IMPORTANT (verified against server build 23601104): Facepunch does NOT use protobuf-net.
# The Companion contracts (AppRequest/AppMessage/AppCameraInput/...) live in Rust.Data.dll,
# namespace `ProtoBuf`, as SilentOrbit-generated `IProto<T>` classes — plain fields plus
# hand-generated Serialize/Deserialize methods, no [ProtoContract]/[ProtoMember] attributes.
# Field numbers/types/names are recovered downstream by parsing those methods (Step 3), not
# by reflection. See proto-refresh-plan.md and §6 notes.
#
# Self-installs ilspycmd at a PINNED version into a script-local tool dir. The version is
# pinned deliberately: the decompiled *shape* of Deserialize is what ProtoGen parses, so an
# unpinned decompiler makes the whole pipeline non-reproducible — a silent decompiler upgrade
# between runs can change field recovery without any server change. Bump ILSPY_VERSION
# consciously, and re-run to confirm the proto is unchanged.
#
# Usage:
#   ./2-decompile.sh --list   List candidate assemblies/types holding AppRequest/AppMessage.
#   ./2-decompile.sh          Decompile the contract assembly into ./decompiled.
#
# Env:
#   CONTRACT_DLL   Assembly to decompile (default: Rust.Data.dll). Override if a future Rust
#                  update relocates the App* contracts.
#   ILSPY_VERSION  ilspycmd version to use (default: the pinned version below).

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
RDS_DIR="${SCRIPT_DIR}/rds"
OUT_DIR="${SCRIPT_DIR}/decompiled"
MANAGED_DIR="${RDS_DIR}/RustDedicated_Data/Managed"
CONTRACT_DLL="${CONTRACT_DLL:-${MANAGED_DIR}/Rust.Data.dll}"

# Pinned decompiler — see the header note. Not "latest": reproducibility is the point.
ILSPY_VERSION="${ILSPY_VERSION:-11.0.0.9375}"
ILSPY_DIR="${SCRIPT_DIR}/.tools/ilspycmd-${ILSPY_VERSION}"

if [[ ! -d "${MANAGED_DIR}" ]]; then
    echo "ERROR: ${MANAGED_DIR} not found. Run ./1-fetch-server.sh first." >&2
    exit 1
fi

# Ensure the *pinned* ilspycmd is available. Installed into a script-local tool dir rather
# than -g so an ambient global ilspycmd of a different version cannot silently change the
# decompiled output (and hence the regenerated proto).
ILSPY_BIN="${ILSPY_DIR}/ilspycmd"
if [[ ! -x "${ILSPY_BIN}" ]]; then
    echo ">> installing pinned ilspycmd ${ILSPY_VERSION} into ${ILSPY_DIR}"
    dotnet tool install ilspycmd --version "${ILSPY_VERSION}" --tool-path "${ILSPY_DIR}" >/dev/null
fi
echo ">> ilspycmd version: ${ILSPY_VERSION}"

# --list: which managed assemblies *define* the companion contracts? (strings is enough to
# spot the type names in metadata; Rust.Data.dll is the expected home.)
if [[ "${1:-}" == "--list" ]]; then
    echo ">> Assemblies whose metadata mentions AppRequest/AppMessage:"
    for d in "${MANAGED_DIR}"/*.dll; do
        # grep -c (not -q): drains stdin so `strings` can't take SIGPIPE, which under
        # `set -o pipefail` would otherwise mask the match. `|| true` keeps set -e happy.
        n="$(strings -n 6 "$d" 2>/dev/null | grep -c "AppRequest" || true)"
        if [[ "${n}" -gt 0 ]]; then
            echo "   HIT: $d"
        fi
    done
    echo ">> (the one that *defines* them — has 'public class AppMessage : ... IProto<AppMessage>' —"
    echo ">>  is the contract assembly; set CONTRACT_DLL to override the Rust.Data.dll default.)"
    exit 0
fi

if [[ ! -f "${CONTRACT_DLL}" ]]; then
    echo "ERROR: contract assembly not found: ${CONTRACT_DLL}" >&2
    echo "       Run './2-decompile.sh --list' to locate it, then set CONTRACT_DLL." >&2
    exit 1
fi

echo ">> Decompiling ${CONTRACT_DLL} into ${OUT_DIR}"
rm -rf "${OUT_DIR}"
mkdir -p "${OUT_DIR}"

# ilspycmd emits one Rust.Data.decompiled.cs for the whole assembly; Step 3 scopes to the
# companion subset (App* roots + transitive closure within namespace ProtoBuf).
"${ILSPY_BIN}" "${CONTRACT_DLL}" -o "${OUT_DIR}"

# Stamp the decompiler version next to its output: the regenerated proto is only meaningful
# relative to the decompiler that produced the input ProtoGen parses.
echo "${ILSPY_VERSION}" > "${OUT_DIR}/.ilspy-version"

# Sanity check: the SilentOrbit contract types are present (NOT protobuf-net attributes).
if ! grep -rql 'IProto<AppMessage>\|class AppMessage' "${OUT_DIR}"; then
    echo "ERROR: AppMessage / IProto<AppMessage> not found in decompiled output." >&2
    echo "       Wrong assembly? Run './2-decompile.sh --list' and set CONTRACT_DLL." >&2
    exit 1
fi

echo ">> OK. Decompiled contracts under ${OUT_DIR}"
echo ">> Next: ./update-proto.sh (or 'dotnet run --project ProtoGen') to regenerate the .proto."
