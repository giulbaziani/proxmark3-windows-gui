<div align="center">

<img src="docs/assets/icon.png" alt="Proxmark3 Windows GUI" width="120" />

# Proxmark3 Windows GUI

### Install and run a Proxmark3 on Windows — one `.exe`, no terminal, no commands.

Download one file, press **Install everything**, and the app sets up WSL2, Ubuntu, the USB
bridge, the Proxmark3 client and the firmware by itself. Then you read and clone cards from buttons.

<br/>

[![Download](https://img.shields.io/badge/⬇_Download-latest_.exe-0062cc?style=for-the-badge)](../../releases/latest)
&nbsp;
[![Build](https://img.shields.io/badge/build-passing-16a048?style=for-the-badge)](../../actions)

![Platform](https://img.shields.io/badge/Windows-10%20%7C%2011-0078D6?logo=windows&logoColor=white)
![.NET](https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet&logoColor=white)
![WinForms](https://img.shields.io/badge/UI-WinForms-5C2D91)
![License](https://img.shields.io/badge/license-MIT-black)
![Single file](https://img.shields.io/badge/self--contained-single%20.exe-success)

</div>

---

## ❓ Why this exists

Running a Proxmark3 on Windows is normally a long terminal session: enable WSL, install Ubuntu,
install a dozen apt packages, write a `Makefile.platform`, compile for twenty minutes, flash the
firmware, bind the USB device with `usbipd`, then memorise `pm3` commands.

**This app does every one of those steps for you**, and puts the common jobs behind buttons. Anyone
who buys the device can download the `.exe` and be cloning cards without ever opening a terminal.

Built for the **Proxmark3 Easy (256 KB)** board, and works with any Proxmark3 the
[Iceman client](https://github.com/RfidResearchGroup/proxmark3) supports.

## ✨ Features

- 🚀 **One-click setup** — installs WSL2, a private Ubuntu, usbipd-win, build tools, source and
  firmware. Resumable, with a live log. Survives reboots.
- 🔌 **Automatic device handling** — finds the Proxmark3 and hands its USB port to Linux; no
  `usbipd` commands.
- 💳 **13.56 MHz cloning** — Mifare Classic → magic card, with UID verification and automatic
  restore of non-default sector keys (the manual step that usually costs an evening).
- 📻 **125 kHz cloning** — EM4100 → T5577, with a read-back check.
- 💾 **Local backups with history** — every backup is kept on your PC under AppData and listed with
  the **date it was taken**, UID, type and size. Restore, export or delete from the list.
- 🖥️ **Console** — any `pm3` command, with the common ones one click away, plus a Linux shell.
- 🎛️ **Admin-aware, responsive UI** — self-elevates with a UAC prompt, scales to any resolution,
  locks controls while working and re-enables them only when a job finishes.

## ⬇️ Install (for users)

1. Download **`Proxmark3Gui.exe`** from the [latest release](../../releases/latest).
2. Double-click it and accept the **administrator** prompt (it needs it to attach USB to Linux).
3. On the **Setup** tab press **Install everything** and leave it running — the first run downloads
   ~300 MB and compiles for 5–20 minutes.
4. Plug in the Proxmark3, open **Device** → **Detect** → **Connect to Linux**.
5. Flash the firmware once from the Setup tab, then reconnect.

That's it — the clone and backup tabs work from there. You do **not** need .NET, Visual Studio,
Git, Ubuntu from the Store, or any terminal knowledge.

## 🧭 The tabs

| Tab | What it does |
| --- | --- |
| **Setup** | One-click install of the whole toolchain. Resumable, live log. |
| **Device** | Detect the Proxmark3, attach it to Linux, test the link, check the antennas. |
| **Clone 13.56 MHz** | Read a Mifare original, write it to a magic card, verify, fix sector keys. |
| **Clone 125 kHz** | Read an EM4100, write it to a T5577, verify. |
| **Backups** | Dated local backup history: restore, export a copy, view, delete. |
| **Console** | Any `pm3` command or Linux shell command. |
| **Settings** | Environment, source repo, dump folder, board type, maintenance. |

## 🛠️ Build it yourself

Requires the [.NET 8 SDK](https://dotnet.microsoft.com/download) (to build, not to run).

```bash
dotnet publish src/Proxmark3Gui/Proxmark3Gui.csproj -c Release -o publish
```

The result is a single self-contained `publish/Proxmark3Gui.exe` (~63 MB) that runs with no .NET
installation. The version shown everywhere comes from the [`VERSION`](VERSION) file — edit it,
rebuild, done.

## 📚 Documentation

| Doc | For |
| --- | --- |
| [User guide](docs/USER-GUIDE.md) | Every tab and button, step by step |
| [Troubleshooting](docs/TROUBLESHOOTING.md) | What each error means and how to clear it |
| [Developer guide](docs/DEVELOPER.md) | Architecture, versioning, how to extend and release |
| [Dependencies & mirrors](docs/DEPENDENCIES.md) | What it installs and how it is mirrored |
| [Manual setup reference](docs/MANUAL-SETUP.md) | The terminal procedure the app automates |
| [ქართული გზამკვლევი](docs/SETUP-GUIDE-KA.md) | The original Georgian setup guide |

## 🧩 Requirements

- Windows 10 version 2004 (build 19041) or newer, or Windows 11
- Virtualization enabled in BIOS/UEFI (WSL2 needs it)
- An administrator account
- ~6 GB free disk space and an internet connection for the first run

## 📦 Dependencies

This repository contains only the Windows app. Everything it installs at runtime is pulled from
upstream and **mirrored into this account**, so a deleted or renamed upstream cannot break installs
(see [docs/DEPENDENCIES.md](docs/DEPENDENCIES.md)).

| Component | Upstream | Licence |
| --- | --- | --- |
| Proxmark3 client & firmware | [RfidResearchGroup/proxmark3](https://github.com/RfidResearchGroup/proxmark3) | GPL-3.0 |
| USB/IP bridge for WSL | [dorssel/usbipd-win](https://github.com/dorssel/usbipd-win) | GPL-2.0 |
| Ubuntu 24.04 WSL image | [Canonical cloud images](https://cloud-images.ubuntu.com/wsl/) | various |

## ⚖️ Legal

Use this only on cards and systems you own or are explicitly authorised to test. Cloning access
credentials you do not control is illegal in most countries. The authors accept no responsibility
for misuse.

## 📄 License

[MIT](LICENSE) for this app. The tools it installs keep their own licences.

<div align="center">
<sub>Not affiliated with RfidResearchGroup, Proxmark, or Canonical.</sub>
</div>
