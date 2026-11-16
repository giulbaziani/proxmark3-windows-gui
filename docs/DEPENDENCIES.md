# Dependencies and mirrors

This repository holds **only the Windows GUI**. The app does not bundle the Proxmark3 source,
usbipd-win, or Ubuntu — those are large, separately licensed, and change often, so they are
fetched at install time instead. To protect against an upstream being deleted, renamed, or made
private, each one is **mirrored into this account** and the app points at the mirror first, with
the upstream as a fallback.

## What the app pulls in at runtime

| Component | Where the app gets it | Upstream | Licence |
| --- | --- | --- | --- |
| Proxmark3 client + firmware | git clone of your mirror `proxmark3-mirror`, then upstream | [RfidResearchGroup/proxmark3](https://github.com/RfidResearchGroup/proxmark3) | GPL-3.0 |
| usbipd-win | winget package `dorssel.usbipd-win` | [dorssel/usbipd-win](https://github.com/dorssel/usbipd-win) | GPL-2.0 |
| Ubuntu 24.04 WSL rootfs | Canonical cloud image URL | [cloud-images.ubuntu.com/wsl](https://cloud-images.ubuntu.com/wsl/) | Ubuntu licences |

The git mirror is the one most worth owning, because it is the only one the build compiles from.
winget and the Ubuntu image are hosted by Microsoft and Canonical and are effectively permanent,
but the mirror script can snapshot them too if you want full control.

## Setting up the git mirror (one time)

1. Create an **empty** repository on your account named `proxmark3-mirror`.
2. Run the sync script with your username:

   ```powershell
   scripts/sync-mirrors.ps1 -Owner YOUR_GITHUB_USERNAME
   ```

   It clones the upstream as a bare mirror and pushes every branch and tag into your repo.
3. Put the same username into the app:
   - edit `src/Proxmark3Gui/Core/AppSettings.cs` → `Defaults.MirrorOwner`, **or**
   - set the **Source repository** field on the app's Settings tab.

## Keeping the mirror current

Re-run `scripts/sync-mirrors.ps1 -Owner YOUR_GITHUB_USERNAME` whenever you want to pick up upstream
changes. It is a fast-forward mirror push; it never rewrites your own commits because the mirror
holds nothing but upstream history. A scheduled GitHub Action can do this automatically — see
`.github/workflows/mirror.yml`.

## If you would rather vendor the whole source

The app can build from any git URL, including a full (non-mirror) copy you commit into a repository
of your own. Point **Source repository** at it. Note the Proxmark3 tree is ~200 MB and GPL-3.0, so
committing it in-tree means this repository inherits that size and that licence obligation for the
vendored part. The mirror approach keeps this repo small and MIT while still guaranteeing you own a
copy of the code.
