#!/usr/bin/env bash

./artifacts/bin/coreclr/linux.x64.Debug/ilc/ilc @./artifacts/bin/repro/x64/Debug/compile-with-Debug-libs.rsp "$@"
