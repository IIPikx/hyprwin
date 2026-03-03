# ╔══════════════════════════════════════════════════════════════╗
# ║           Winget Manifest for HyprWin                       ║
# ║   Submit this to microsoft/winget-pkgs as a PR.             ║
# ╚══════════════════════════════════════════════════════════════╝
#
# Winget requires 3 manifest files in a folder structure like:
#   manifests/h/HyprWin/HyprWin/1.0.0/
#     HyprWin.HyprWin.yaml              (version manifest)
#     HyprWin.HyprWin.installer.yaml    (installer manifest)
#     HyprWin.HyprWin.locale.en-US.yaml (locale manifest)
#
# Steps to publish to winget:
#   1. Host the installer .exe on GitHub Releases (public URL)
#   2. Get the SHA256 hash: certutil -hashfile HyprWin-Setup-1.0.0.exe SHA256
#   3. Fill in InstallerUrl and InstallerSha256 below
#   4. Fork https://github.com/microsoft/winget-pkgs
#   5. Create the folder structure and add these files
#   6. Submit a PR — the winget bot validates automatically
#
# Alternatively, use: winget create <InstallerUrl>
#   This auto-generates the manifests interactively.
#
# ─────────────────────────────────────────────────────────────────
