#!/usr/bin/env bash
set -euo pipefail

BXL="$HOME/src/BuildXL/Out/Selfhost/Dev/bxl"

if [[ ! -x "$BXL" ]]; then
    echo "Error: bxl not found at $BXL" >&2
    exit 1
fi

echo "Setting eBPF capabilities on $BXL"
sudo setcap 'cap_sys_admin,cap_bpf,cap_perfmon=ep' "$BXL"
echo "Done. You can now run bxl with /EnableLinuxEBPFSandbox+"
