# Troubleshooting

What each message means and how to clear it. Copy the log (button in the Log panel) when you open
an issue.

| Symptom | Cause / fix |
| --- | --- |
| "Administrator rights are required" | The app was not started elevated. Close it and use **Run as administrator**. |
| Setup says Windows must restart | WSL was just enabled. Restart Windows, start the app again — setup resumes. |
| `make: command not found` / build fails on packages | The build tools step did not finish. Run **Re-check**, then **Install everything** again. |
| "The firmware does not fit in 256 KB" | Keep **Proxmark3 Easy** on in Settings, or add more `SKIP_*` lines to `Makefile.platform`, then **Rebuild from source**. |
| Detect finds nothing | Replug the device, avoid USB hubs, press **Detect** again. Confirm it appears in Windows Device Manager first. |
| Connect succeeds but "Linux sees no serial port" | usbipd attached but `/dev/ttyACM0` did not appear. Disconnect, replug, detect and connect again. |
| `invalid serial port /dev/ttyACM0` | The device is not attached to the environment. Use the Device tab to connect it. |
| "Proxmark3 not found" while flashing | The device re-enumerated mid-flash. Hold the button while plugging it in to force the bootloader, attach again, retry. |
| UID did not change after a write | The target is not a magic card, or two cards are on the antenna. Use one magic/CUID card. |
| A real reader rejects the clone | A sector key differs from default. The HF flow restores those automatically — check the log, or re-run step 2. |
| LF write does not stick | The card is a read-only EM4100, not a writable T5577. Use **Detect T5577** to confirm. |
| Everything is slow or the environment acts strange | **Shut WSL down** in Settings, then retry; WSL restarts on the next command. |

If a step is stuck, **Stop**, then **Re-check** to see the true state before retrying.
