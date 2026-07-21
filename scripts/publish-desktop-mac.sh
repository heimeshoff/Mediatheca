#!/usr/bin/env bash
# Publish the Photino desktop shell as a self-contained macOS app (Apple
# Silicon). No .NET runtime install required on the target machine.
#
# Run from the repo root. Build the client first (`npm run build`) so
# deploy/public exists — Desktop.fsproj copies it into the publish output.
#
# NOTE: the *build* of this publish command was verified cross-compiled from
# a Windows dev machine (infrastructure-w8fnp spike). It has never been run
# on an actual Mac — treat runtime behavior (webview rendering, WebView2's
# macOS equivalent WKWebView, code signing/Gatekeeper prompts) as unverified
# until someone does. See ADR-0018.
#
# Usage: ./scripts/publish-desktop-mac.sh [out-dir]

set -euo pipefail

OUT_DIR="${1:-publish/desktop-osx-arm64}"

dotnet publish src/Desktop/Desktop.fsproj \
    -c Release \
    -r osx-arm64 \
    --self-contained \
    -o "$OUT_DIR"

echo "Published to $OUT_DIR — run $OUT_DIR/Desktop"
echo "(Unsigned build: macOS Gatekeeper will likely block first launch — right-click > Open, or codesign/notarize for distribution.)"
