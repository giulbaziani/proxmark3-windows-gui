# Dependencies and mirrors

This repository holds **only the Windows GUI**. The app does not bundle the Proxmark3 source,
usbipd-win, or Ubuntu — those are large, separately licensed, and change often, so they are
fetched at install time instead. To protect against an upstream being deleted, renamed, or made
private, the Proxmark3 source is pulled from a **fork on this account**, with the real upstream as
a fallback.

## What the app pulls in at runtime

| Component | Where the app gets it | Upstream | Licence |
| --- | --- | --- | --- |
| Proxmark3 client + firmware | git clone of the fork [`giulbaziani/proxmark3`](https://github.com/giulbaziani/proxmark3), then upstream | [RfidResearchGroup/proxmark3](https://github.com/RfidResearchGroup/proxmark3) | GPL-3.0 |
| usbipd-win | winget package `dorssel.usbipd-win` | [dorssel/usbipd-win](https://github.com/dorssel/usbipd-win) | GPL-2.0 |
| Ubuntu 24.04 WSL rootfs | Canonical cloud image URL | [cloud-images.ubuntu.com/wsl](https://cloud-images.ubuntu.com/wsl/) | Ubuntu licences |

The fork is the one most worth owning, because it is the only one the build compiles from. winget
and the Ubuntu image are hosted by Microsoft and Canonical and are effectively permanent.

## The fork (already set up)

The app's default source is the fork `https://github.com/giulbaziani/proxmark3.git`
(`Defaults.SourceRepoUrl` in `src/Proxmark3Gui/Core/AppSettings.cs`). To use a different fork,
change `Defaults.MirrorOwner` there, or set the **Source repository** field on the app's Settings
tab. If the fork is ever unreachable, the app automatically falls back to the upstream repository.

## Keeping the fork current

A GitHub fork is the simplest to keep in sync:

- press **Sync fork** on the fork's GitHub page, or
- run `gh repo sync giulbaziani/proxmark3`, or
- run `scripts/sync-mirrors.ps1 -Owner giulbaziani` (mirror-pushes every branch and tag), or
- let `.github/workflows/mirror.yml` do it weekly (needs a `MIRROR_TOKEN` secret).

## If you would rather vendor the whole source

The app can build from any git URL, including a full (non-mirror) copy you commit into a repository
of your own. Point **Source repository** at it. Note the Proxmark3 tree is ~200 MB and GPL-3.0, so
committing it in-tree means this repository inherits that size and that licence obligation for the
vendored part. The mirror approach keeps this repo small and MIT while still guaranteeing you own a
copy of the code.
