#!/usr/bin/env bash
# Wrapper for the restore pip. Runs two steps:
# 1. Arcade-based restore (libs.oob+libs.tests) for src library projects
# 2. Explicit test project restore for individual test projects
set -euo pipefail

BUILD_SH="$1"
RESTORE_TESTS_SH="$2"

echo "=== Step 1: Restoring OOB library projects ==="
"$BUILD_SH" --restore --subset libs.oob+libs.tests

echo "=== Step 2: Restoring test projects ==="
"$RESTORE_TESTS_SH"

echo "=== Restore complete ==="
