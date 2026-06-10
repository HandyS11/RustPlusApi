#!/usr/bin/env bash
#
# Orchestrates the authoritative proto refresh (§6, Method A), end to end:
#   1. fetch the Rust dedicated server   (SteamCMD, app 258550)
#   2. decompile the contract assembly   (ilspycmd -> Rust.Data.decompiled.cs)
#   3. regenerate RustPlusContracts.proto (ProtoGen: parse SilentOrbit C# -> proto)
#   4. diff against the committed proto and surface any changes
#
# The diff is the deliverable: review it, then apply the genuine wire changes to the
# committed proto and open a PR (see proto-refresh-plan.md / README.md).
#
# Exit status: 0 = no changes; 1 = changes found (review out/proto.diff); >1 = error.
#
# Env toggles (handy when iterating — the fetch is multi-GB and the decompile is slow):
#   SKIP_FETCH=1       reuse the server already under ./rds
#   SKIP_DECOMPILE=1   reuse ./decompiled/Rust.Data.decompiled.cs

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
COMMITTED="${SCRIPT_DIR}/../../src/RustPlusApi/Protobuf/RustPlusContracts.proto"
DECOMPILED="${SCRIPT_DIR}/decompiled/Rust.Data.decompiled.cs"
OUT_DIR="${SCRIPT_DIR}/out"
OUT_PROTO="${OUT_DIR}/RustPlusContracts.proto"
OUT_DIFF="${OUT_DIR}/proto.diff"

if [[ "${SKIP_FETCH:-0}" != "1" ]]; then
    "${SCRIPT_DIR}/1-fetch-server.sh"
fi
if [[ "${SKIP_DECOMPILE:-0}" != "1" || ! -f "${DECOMPILED}" ]]; then
    "${SCRIPT_DIR}/2-decompile.sh"
fi

mkdir -p "${OUT_DIR}"
echo ">> regenerating proto from decompiled contracts"
dotnet run --project "${SCRIPT_DIR}/ProtoGen" -- "${DECOMPILED}" "${COMMITTED}" "${OUT_PROTO}"

# Compare, ignoring trailing-whitespace differences.
if diff -u -Z "${COMMITTED}" "${OUT_PROTO}" > "${OUT_DIFF}"; then
    echo ">> ✅ no changes — the committed proto matches the current server."
    rm -f "${OUT_DIFF}"
    exit 0
fi

CHANGES="$(grep -cE '^[+-][^+-]' "${OUT_DIFF}" || true)"
echo ">> ⚠️  ${CHANGES} changed line(s) vs the committed proto — review ${OUT_DIFF}:"
echo
cat "${OUT_DIFF}"
echo
echo ">> Not all changes are necessarily desired (e.g. deliberate library refinements such as"
echo ">> richer enum typing). Review, apply the genuine wire changes, and open a PR."
exit 1
