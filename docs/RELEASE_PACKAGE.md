# D2Companion Windows Beta Package

This file is copied into the Windows release zip as the end-user README.

## Install

1. Download the `D2Companion-*-win-x64.zip` file from GitHub Releases.
2. Extract the zip to a normal folder, for example:
   `C:\Games\D2Companion`.
3. Run `D2Companion.exe`.

Keep the extracted files together. The executable expects the `wwwroot`,
`data`, and `styx` folders next to it. Runtime artwork is bundled in
`wwwroot\assets\d2companion-assets.d2pack`; this is a packaging cleanup, not
DRM, and no user action is required.

## Requirements

- Windows x64.
- Diablo II Lord of Destruction 1.14d.
- Microsoft Edge WebView2 Runtime for the embedded app window:
  https://developer.microsoft.com/microsoft-edge/webview2/
- Bundled portable Node and Styx dependencies are included for live capture.
  Node.js/npm are not required for live capture in the official Windows zip.
- Proxifier or equivalent if you want to route game traffic through Styx:
  https://www.proxifier.com/

The .NET runtime is bundled in the package. You do not need the .NET SDK to run
the app.

If WebView2 is missing, D2Companion will show a warning and try to open the app
in your default browser instead. The app can still open, and offline MuleLogger import still works without Node, Styx, or Proxifier.

## First Run Data

The release zip does not include a personal SQLite database, logs, or debug
dumps. On first run, D2Companion creates `data\companion.sqlite` inside the
extracted folder.

For offline use, open Accounts, click `Import Mule Files`, paste or enter a
MuleLogger file/folder path, and import. Imported characters and items appear
in My Accounts and Gear Viewer. This does not require Styx or Proxifier.

## Live Capture Notes

D2Companion includes the Styx runtime files needed by the app. Styx uses the
bundled `runtimes\node\node.exe` first and the beta package includes Styx's
runtime dependencies under `styx\node_modules`. Normal users do not need
Node.js or npm installed for live capture in the official Windows zip.

The release package serves Diablo II UI art, item sprites, mercenary portraits,
cursor frames, and tooltip/font assets from `wwwroot\assets\d2companion-assets.d2pack`.
The private development checkout keeps the raw asset folders loose. Public
source exports and Windows release zips keep the runtime artwork in the pack
plus required loose files such as `wwwroot\favicon.ico`.

When D2Companion closes, it stops the Styx process that it started. If live
capture is waiting for a game or currently in game, the app warns before
closing because stopping Styx may disconnect Diablo II from Battle.net when
`Game.exe` is routed through the local proxy. Choose `Cancel` if you want to
leave or close Diablo II first.

Configure Proxifier before connecting Diablo II to Battle.net. Add a SOCKS5
proxy server for `127.0.0.1` port `20676`, add a rule for `Game.exe`, and route
all TCP traffic for that executable through `127.0.0.1:20676`. Then start or
reconnect Diablo II and join a game. If the game was already connected before
the rule existed, reconnect so the traffic uses the local proxy.

If the status says live capture is running but no character appears after
joining a game, open `data\styx.log` in the extracted folder. A healthy managed
release start logs the bundled Node path, the Styx working directory, the local
snapshot endpoint, and `Loaded CompanionBridge`. If `CompanionBridge` is not
loaded or the endpoint is not `http://127.0.0.1:5178/api/ingest/styx/snapshot`,
rebuild or re-extract the release package. If those lines are present, check
the Proxifier rule and reconnect Diablo II.

If `data\styx.log` shows D2GS `:4000` traffic followed by `readUInt* is not a
function` or `br.readUInt32LE is not a function`, the package has a stale or
broken Styx parser. Rebuild the release package and make sure the bundled Styx
runtime contains the current `styx\bin\lib\BitReader.js` and
`styx\bin\lib\NodeBuffer.js` files.

If a later launch cannot start Styx because port `20676` is occupied, check for
a leftover local proxy with `netstat -ano | findstr :20676` and close the
owning process only if you know it belongs to the old D2Companion run. The app
does not kill unrelated `node.exe` processes automatically.

Do not share your generated `data\companion.sqlite`, `data\styx.log`, or
`data\debug` files publicly. They may contain local account, character, or game
session information.

## Current Beta Limitations

- Trade Preview / TradeMaster is still a placeholder.
- Chat Archive persistence is not implemented.
- Outgoing in-game chat packets are not implemented.
- Settings page is not implemented.

## GitHub Releases

Normal users should download the Windows zip from the GitHub Releases panel,
extract the folder, and run `D2Companion.exe`. The source repository is for
developers who want to build the app themselves.

## Unsigned App Notice

This beta package is not code-signed. Windows SmartScreen may warn before first
launch. Review the extracted folder and run it only if you trust the source of
the zip.
