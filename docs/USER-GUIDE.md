# User guide

A short tour of each tab. Use this only on hardware and cards you own or are authorised to test.

---

## Before you start

- Right-click `Proxmark3Gui.exe` → **Run as administrator**. Without elevation the app cannot
  hand the USB device to Linux, and setup cannot enable WSL.
- Keep the **Log** panel visible — every action writes there, and **Copy** gives you text to paste
  into a bug report.
- Any long operation can be stopped with **Stop**.

---

## 1 · Setup

The checklist is the whole install, step by step:

| Step | What happens |
| --- | --- |
| Windows Subsystem for Linux | Enables the WSL2 components and updates the kernel. |
| usbipd-win | Installs the USB/IP bridge through winget. |
| Ubuntu environment | Downloads the Ubuntu 24.04 WSL image and imports it as a private distribution `pm3-ubuntu`. |
| Build tools | Installs the compilers and libraries with apt. |
| Proxmark3 source code | Clones the client/firmware and writes `Makefile.platform` for the 256 KB board. |
| Compile | Builds the client and firmware — minutes, not seconds, on the first run. |
| Flash the firmware | Offered on demand once the rest is done. |

Press **Install everything** and leave it running. Finished steps are remembered, so closing the
app, rebooting, or a failed step all resume instead of starting over. If Windows asks to restart,
restart and start the app again.

Other buttons: **Re-check** re-runs the checks without changing anything; **Rebuild from source**
pulls the latest Proxmark3 code and recompiles; **Flash firmware** writes the built firmware to a
connected device.

Your existing WSL distributions are left untouched — the app only manages its own `pm3-ubuntu`
instance, which runs as root so nothing asks for a password.

## 2 · Device

**Detect** lists what usbipd sees and auto-selects the Proxmark3. **Connect to Linux** attaches the
USB port and confirms the device appears inside the environment. **Test connection** runs
`hw version`. **Antenna check** runs `hw tune`. You connect once per Windows session; a freshly
flashed device gets a new USB id, so detect and connect again after flashing.

## 3 · Clone 13.56 MHz / 4 · Clone 125 kHz

Two-step flows: read the source card, then write to a writable target, with an automatic read-back
check. The HF flow also restores non-default sector keys after the write — the manual step a plain
restore skips. Place one card on the correct antenna at a time; the on-screen notes say which coil
is which and which target cards are appropriate.

## 5 · Backups

Every backup is stored locally on this computer, under
`%APPDATA%\Proxmark3Gui\backups`, and listed in the app with the **date it was taken**, the card
UID, type and size. From the list you can **restore** a backup to a card, **export a copy** to any
folder, **view** its contents, **reveal** the file in Explorer, or **delete** it. "Back up the card
on the antenna" recovers the keys, dumps the card, and adds a timestamped entry automatically. The
list survives restarts, so your backup history is always there.

## 6 · Console

Run any `pm3` command — common ones are in the dropdown — and see the answer in the log. A second
box runs a plain Linux command inside the managed environment for diagnosis.

## 7 · Settings

Environment name, source folder, source repository (your mirror), dump folder, and board type.
Maintenance actions let you forget setup progress, delete the managed environment, or shut WSL down.

See [TROUBLESHOOTING.md](TROUBLESHOOTING.md) when something does not behave.
