using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Proxmark3Gui.Core
{
    public class StepResult
    {
        public bool Ok;
        public bool NeedsReboot;
        public string Message = "";

        public static StepResult Success(string msg = "") { return new StepResult { Ok = true, Message = msg }; }
        public static StepResult Fail(string msg) { return new StepResult { Ok = false, Message = msg }; }
        public static StepResult Reboot(string msg) { return new StepResult { Ok = false, NeedsReboot = true, Message = msg }; }
    }

    public class SetupStep
    {
        public string Id;
        public string Title;
        public string Description;

        /// <summary>True when the step is already satisfied on this machine.</summary>
        public Func<CancellationToken, Task<bool>> Check;

        public Func<Action<string>, CancellationToken, Task<StepResult>> Run;

        /// <summary>The Proxmark3 must be plugged in before this step can run.</summary>
        public bool RequiresDevice;

        /// <summary>Not part of the unattended run; the user starts it explicitly.</summary>
        public bool Manual;
    }

    /// <summary>
    /// Turns the whole written setup guide into a sequence of steps the app performs itself.
    /// Every step is idempotent and remembers that it finished, so the wizard can be re-run
    /// or resumed after a reboot.
    /// </summary>
    public class SetupManager
    {
        private readonly AppSettings _s;
        public List<SetupStep> Steps { get; private set; }

        /// <summary>apt packages required to build the client and the ARM firmware (from the setup guide).</summary>
        public const string AptPackages =
            "git ca-certificates build-essential pkg-config libreadline-dev gcc-arm-none-eabi " +
            "libnewlib-dev qtbase5-dev libbz2-dev liblz4-dev libbluetooth-dev libpython3-dev " +
            "libssl-dev libgd-dev xxd";

        /// <summary>
        /// Proxmark3 Easy has only 256 KB of flash. The full firmware is ~333 KB, so the modules
        /// not needed for card cloning are skipped to fit. What stays: LF (EM4100/T5577) and HF
        /// ISO14443-A (Mifare) — exactly what the clone tabs use. iClass, ISO15693, FeliCa, Legic,
        /// Hitag, EM4x50/70, DESFire-sim, and the HF plot/sniff helpers are dropped.
        /// </summary>
        public const string Easy256KPlatform =
            "PLATFORM=PM3GENERIC\n" +
            "PLATFORM_SIZE=256\n" +
            "LED_ORDER=PM3EASY\n" +
            "STANDALONE=\n" +
            "SKIP_HITAG=1\n" +
            "SKIP_EM4x50=1\n" +
            "SKIP_EM4x70=1\n" +
            "SKIP_ZX8211=1\n" +
            "SKIP_LEGICRF=1\n" +
            "SKIP_FELICA=1\n" +
            "SKIP_NFCBARCODE=1\n" +
            "SKIP_ICLASS=1\n" +
            "SKIP_SEOS=1\n" +
            "SKIP_ISO15693=1\n" +
            "SKIP_ISO14443b=1\n" +
            "SKIP_DESFIRE=1\n" +
            "SKIP_HFSNIFF=1\n" +
            "SKIP_HFPLOT=1\n";

        public SetupManager(AppSettings s)
        {
            _s = s;
            Steps = BuildSteps();
        }

        public string WorkDir
        {
            get { return Path.Combine(AppSettings.Folder, "downloads"); }
        }

        public string DistroInstallDir
        {
            get { return Path.Combine(AppSettings.Folder, "distros", _s.Distro); }
        }

        // ------------------------------------------------------------------ steps

        private List<SetupStep> BuildSteps()
        {
            return new List<SetupStep>
            {
                new SetupStep
                {
                    Id = "wsl",
                    Title = "Windows Subsystem for Linux",
                    Description = "Enables WSL2 — the Linux environment the Proxmark3 client runs in.",
                    Check = async ct => await WslManager.IsWslReadyAsync().ConfigureAwait(false),
                    Run = StepInstallWsl
                },
                new SetupStep
                {
                    Id = "usbipd",
                    Title = "usbipd-win",
                    Description = "Lets Windows hand the Proxmark3's USB port over to Linux.",
                    Check = ct => Task.FromResult(UsbIpdManager.IsInstalled()),
                    Run = StepInstallUsbIpd
                },
                new SetupStep
                {
                    Id = "distro",
                    Title = "Ubuntu environment",
                    Description = "Creates a private Ubuntu 24.04 instance named '" + _s.Distro +
                                  "'. Any distro you already use is left untouched.",
                    Check = async ct => await WslManager.DistroExistsAsync(_s.Distro).ConfigureAwait(false),
                    Run = StepCreateDistro
                },
                new SetupStep
                {
                    Id = "packages",
                    Title = "Build tools",
                    Description = "Installs the compilers and libraries needed to build the client and firmware.",
                    Check = async ct =>
                    {
                        var r = await WslManager.RunInDistroAsync(_s,
                            "command -v arm-none-eabi-gcc >/dev/null && command -v gcc >/dev/null && " +
                            "pkg-config --exists readline && echo PKGS_OK", null, 60000, ct).ConfigureAwait(false);
                        return r.OutputContains("PKGS_OK");
                    },
                    Run = StepInstallPackages
                },
                new SetupStep
                {
                    Id = "source",
                    Title = "Proxmark3 source code",
                    Description = "Clones the client and firmware source and writes the board configuration.",
                    Check = async ct =>
                    {
                        var r = await WslManager.RunInDistroAsync(_s,
                            "test -f " + Q(_s.Pm3Dir) + "/Makefile && echo SRC_OK", null, 60000, ct).ConfigureAwait(false);
                        return r.OutputContains("SRC_OK");
                    },
                    Run = StepFetchSource
                },
                new SetupStep
                {
                    Id = "build",
                    Title = "Compile",
                    Description = "Builds the pm3 client and the firmware image. Takes 5-20 minutes the first time.",
                    Check = async ct =>
                    {
                        var r = await WslManager.RunInDistroAsync(_s,
                            "test -x " + Q(_s.Pm3Dir) + "/pm3 && test -f " + Q(_s.Pm3Dir) +
                            "/client/proxmark3 && echo BUILD_OK", null, 60000, ct).ConfigureAwait(false);
                        return r.OutputContains("BUILD_OK");
                    },
                    Run = StepBuild
                },
                new SetupStep
                {
                    Id = "flash",
                    Title = "Flash the firmware",
                    Description = "Writes the freshly built firmware to the device. Plug the Proxmark3 in first.",
                    RequiresDevice = true,
                    Manual = true,
                    Check = ct => Task.FromResult(false), // always offered; flashing is the user's call
                    Run = StepFlash
                }
            };
        }

        private static string Q(string s) { return WslManager.ShellQuote(s); }

        // ------------------------------------------------------------- implementations

        private async Task<StepResult> StepInstallWsl(Action<string> log, CancellationToken ct)
        {
            if (!Elevation.IsAdministrator())
                return StepResult.Fail("Administrator rights are required. Restart the app as administrator.");

            if (!WslManager.WslExeExists())
                return StepResult.Fail(
                    "wsl.exe was not found. This app needs Windows 10 version 2004 (build 19041) or newer.");

            log("[i] Installing the WSL2 components...");
            var r = await WslManager.InstallWslAsync(log, ct).ConfigureAwait(false);

            if (WslManager.RebootLikelyRequired(r) && !await WslManager.IsWslReadyAsync().ConfigureAwait(false))
                return StepResult.Reboot("Windows needs to restart to finish enabling WSL. " +
                                         "Reboot, then start this app again — setup resumes where it stopped.");

            log("[i] Updating the WSL kernel...");
            await WslManager.UpdateWslAsync(log, ct).ConfigureAwait(false);

            if (await WslManager.IsWslReadyAsync().ConfigureAwait(false))
                return StepResult.Success("WSL2 is ready.");

            return StepResult.Reboot("WSL was installed but is not active yet. Restart Windows and run setup again.");
        }

        private async Task<StepResult> StepInstallUsbIpd(Action<string> log, CancellationToken ct)
        {
            if (UsbIpdManager.IsInstalled()) return StepResult.Success("Already installed.");

            var r = await UsbIpdManager.InstallAsync(log, ct).ConfigureAwait(false);
            if (UsbIpdManager.IsInstalled()) return StepResult.Success("usbipd-win installed.");

            return StepResult.Fail(
                "Automatic installation failed (exit code " + r.ExitCode + "). Install usbipd-win by hand from " +
                Defaults.UsbIpdReleasesUrl + " and run this step again.");
        }

        private async Task<StepResult> StepCreateDistro(Action<string> log, CancellationToken ct)
        {
            if (await WslManager.DistroExistsAsync(_s.Distro).ConfigureAwait(false))
                return StepResult.Success("The '" + _s.Distro + "' environment already exists.");

            Directory.CreateDirectory(WorkDir);
            string tar = Path.Combine(WorkDir, "ubuntu-24.04-wsl-rootfs.tar.gz");

            if (!File.Exists(tar) || new FileInfo(tar).Length < 10 * 1024 * 1024)
            {
                log("[i] Downloading Ubuntu 24.04 (about 300 MB, once)...");
                if (!await Downloader.DownloadAsync(Defaults.UbuntuRootfsUrl, tar, log, ct).ConfigureAwait(false))
                    return StepResult.Fail("Could not download the Ubuntu image. Check your internet connection.");
            }
            else
            {
                log("[i] Reusing the Ubuntu image already downloaded.");
            }

            var r = await WslManager.ImportDistroAsync(_s.Distro, tar, DistroInstallDir, log, ct).ConfigureAwait(false);
            if (!r.Ok && !await WslManager.DistroExistsAsync(_s.Distro).ConfigureAwait(false))
                return StepResult.Fail("Importing the Ubuntu environment failed (exit code " + r.ExitCode + ").");

            // Basic sanity + make sure the default user is root so nothing ever asks for a password.
            var probe = await WslManager.RunInDistroAsync(_s, "id -un", log, 120000, ct).ConfigureAwait(false);
            if (!probe.OutputContains("root"))
                log("[!] Warning: commands are not running as root inside '" + _s.Distro + "'.");

            return StepResult.Success("The '" + _s.Distro + "' environment is ready.");
        }

        private async Task<StepResult> StepInstallPackages(Action<string> log, CancellationToken ct)
        {
            string script =
                "export DEBIAN_FRONTEND=noninteractive; " +
                "apt-get update -y && " +
                "apt-get install -y --no-install-recommends " + AptPackages;

            log("[i] Installing build tools (a few minutes)...");
            var r = await WslManager.RunInDistroAsync(_s, script, log, 40 * 60 * 1000, ct).ConfigureAwait(false);
            if (!r.Ok) return StepResult.Fail("apt failed (exit code " + r.ExitCode + "). See the log above.");
            return StepResult.Success("Build tools installed.");
        }

        private async Task<StepResult> StepFetchSource(Action<string> log, CancellationToken ct)
        {
            string dir = Q(_s.Pm3Dir);
            string fallback = _s.FallbackRepoUrl;

            // Only use the mirror when it has actually been configured. An unconfigured mirror URL
            // (the "CHANGE-ME" placeholder) points at a repository that does not exist, and over
            // HTTPS git would block forever waiting for credentials — so we skip it entirely.
            string primary = _s.SourceRepoUrl;
            bool mirrorConfigured =
                !string.IsNullOrWhiteSpace(primary) &&
                primary.IndexOf("CHANGE-ME", StringComparison.OrdinalIgnoreCase) < 0 &&
                !primary.Equals(fallback, StringComparison.OrdinalIgnoreCase);

            if (mirrorConfigured)
                log("[i] Fetching the Proxmark3 source from your mirror (upstream as fallback)...");
            else
                log("[i] No mirror configured — fetching the Proxmark3 source from upstream...");

            // GIT_TERMINAL_PROMPT=0 makes git fail fast instead of hanging on a credential prompt,
            // so a bad URL falls through to the next clone instead of freezing the whole setup.
            string cloneCmd;
            if (mirrorConfigured)
                cloneCmd = "git clone --depth 1 " + Q(primary) + " " + dir +
                           " || git clone --depth 1 " + Q(fallback) + " " + dir;
            else
                cloneCmd = "git clone --depth 1 " + Q(fallback) + " " + dir;

            // A completed clone has both a .git folder and the top-level Makefile. Anything else
            // (e.g. a clone interrupted by Stop) is treated as incomplete and wiped before retrying.
            string script =
                "export GIT_TERMINAL_PROMPT=0 GCM_INTERACTIVE=never GIT_ASKPASS=/bin/true; " +
                "if [ -d " + dir + "/.git ] && [ -f " + dir + "/Makefile ]; then " +
                "  cd " + dir + " && (git fetch --all --tags && git reset --hard origin/HEAD 2>/dev/null || git pull --ff-only); " +
                "else " +
                "  rm -rf " + dir + " && mkdir -p \"$(dirname " + dir + ")\" && " + cloneCmd + "; " +
                "fi";

            var r = await WslManager.RunInDistroAsync(_s, script, log, 30 * 60 * 1000, ct).ConfigureAwait(false);
            if (!r.Ok) return StepResult.Fail("git clone failed (exit code " + r.ExitCode + ").");

            var cfg = await WritePlatformConfigAsync(log, ct).ConfigureAwait(false);
            if (!cfg.Ok) return cfg;

            return StepResult.Success("Source code ready in " + _s.Pm3Dir + ".");
        }

        /// <summary>
        /// Writes (or removes) Makefile.platform to match the current board setting. Called from
        /// both the source step and the build step, so the firmware is always compiled with the
        /// right module set even if the source was cloned by an older version.
        /// </summary>
        private async Task<StepResult> WritePlatformConfigAsync(Action<string> log, CancellationToken ct)
        {
            string dir = Q(_s.Pm3Dir);
            if (_s.Easy256K)
            {
                log("[i] Writing Makefile.platform for a 256 KB Proxmark3 Easy...");
                string write =
                    "cat > " + dir + "/Makefile.platform << 'PM3EOF'\n" + Easy256KPlatform + "PM3EOF\n" +
                    "echo '--- Makefile.platform ---'; cat " + dir + "/Makefile.platform";
                var w = await WslManager.RunInDistroAsync(_s, write, log, 60000, ct).ConfigureAwait(false);
                if (!w.Ok) return StepResult.Fail("Could not write Makefile.platform.");
            }
            else
            {
                await WslManager.RunInDistroAsync(_s, "rm -f " + dir + "/Makefile.platform", log, 60000, ct)
                    .ConfigureAwait(false);
            }
            return StepResult.Success();
        }

        private async Task<StepResult> StepBuild(Action<string> log, CancellationToken ct)
        {
            string dir = Q(_s.Pm3Dir);

            // Always refresh the platform config first, so a build never uses a stale module set
            // (e.g. one left by an earlier version that made the firmware too large for 256 KB).
            var cfg = await WritePlatformConfigAsync(log, ct).ConfigureAwait(false);
            if (!cfg.Ok) return cfg;

            log("[i] Compiling. The first build takes 5-20 minutes — the log keeps moving.");

            // make clean is required after a platform change so every module is rebuilt.
            string script = "cd " + dir + " && make clean >/dev/null 2>&1; make -j\"$(nproc)\"";
            var r = await WslManager.RunInDistroAsync(_s, script, log, 90 * 60 * 1000, ct).ConfigureAwait(false);

            if (r.OutputContains("image is too large") || r.OutputContains("will not fit"))
                return StepResult.Fail(
                    "The firmware does not fit in 256 KB. Add more SKIP_* lines to Makefile.platform " +
                    "(for example SKIP_ICLASS=1, SKIP_ISO15693=1) and build again.");

            if (!r.Ok) return StepResult.Fail("The build failed (exit code " + r.ExitCode + "). See the log above.");
            return StepResult.Success("Client and firmware built.");
        }

        private async Task<StepResult> StepFlash(Action<string> log, CancellationToken ct)
        {
            string dir = Q(_s.Pm3Dir);

            var port = await WslManager.RunInDistroAsync(_s,
                "ls /dev/ttyACM* 2>/dev/null || true", log, 30000, ct).ConfigureAwait(false);
            if (!port.OutputContains("/dev/ttyACM"))
                return StepResult.Fail(
                    "No Proxmark3 serial port inside Linux. Go to the Device tab, attach the device, then retry.");

            log("[i] Flashing — do not unplug the device.");
            var r = await WslManager.RunInDistroAsync(_s,
                "cd " + dir + " && ./pm3-flash-all", log, 20 * 60 * 1000, ct).ConfigureAwait(false);

            if (r.OutputContains("Have a nice day"))
                return StepResult.Success("Firmware written. The device re-enumerated, so attach it again.");

            if (r.OutputContains("Proxmark3 not found"))
                return StepResult.Fail(
                    "The device disappeared mid-flash. Hold the button down while plugging it in to force the " +
                    "bootloader, attach it again on the Device tab, then retry.");

            return StepResult.Fail("Flashing did not report success (exit code " + r.ExitCode + ").");
        }

        // ------------------------------------------------------------------ driver

        public async Task<bool> RunAllAsync(
            Action<SetupStep, string> onStepStart,
            Action<SetupStep, StepResult> onStepDone,
            Action<string> log,
            CancellationToken ct)
        {
            foreach (var step in Steps)
            {
                if (step.Manual) continue;
                if (ct.IsCancellationRequested) return false;

                onStepStart?.Invoke(step, "checking");
                bool done = false;
                try { done = await step.Check(ct).ConfigureAwait(false); }
                catch { done = false; }

                if (done)
                {
                    _s.MarkStepDone(step.Id);
                    onStepDone?.Invoke(step, StepResult.Success("Already done."));
                    continue;
                }

                onStepStart?.Invoke(step, "running");
                log?.Invoke("");
                log?.Invoke("===== " + step.Title + " =====");

                StepResult res;
                try { res = await step.Run(log, ct).ConfigureAwait(false); }
                catch (OperationCanceledException) { return false; }
                catch (Exception ex) { res = StepResult.Fail(ex.Message); }

                if (res.Ok) _s.MarkStepDone(step.Id);
                onStepDone?.Invoke(step, res);

                if (!res.Ok) return false;
            }
            return true;
        }
    }
}
