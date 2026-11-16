using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Proxmark3Gui.Core
{
    /// <summary>
    /// User settings, persisted as JSON in %APPDATA%\Proxmark3Gui\settings.json.
    /// Nothing secret is stored here: the managed WSL distro runs as root, so the app
    /// never needs a sudo password.
    /// </summary>
    public class AppSettings
    {
        /// <summary>Name of the WSL distribution the app creates and owns.</summary>
        public string Distro { get; set; } = "pm3-ubuntu";

        /// <summary>Use an existing distro instead of the managed one.</summary>
        public bool UseExistingDistro { get; set; } = false;

        /// <summary>Linux user the commands run as. The managed distro has only root.</summary>
        public string LinuxUser { get; set; } = "root";

        /// <summary>Where the proxmark3 source lives inside the distro.</summary>
        public string Pm3Dir { get; set; } = "/opt/proxmark3";

        /// <summary>Git remote the firmware/client source is cloned from (your mirror by default).</summary>
        public string SourceRepoUrl { get; set; } = Defaults.SourceRepoUrl;

        /// <summary>Upstream, used only as a fallback when the mirror is unreachable.</summary>
        public string FallbackRepoUrl { get; set; } = Defaults.FallbackRepoUrl;

        /// <summary>PM3GENERIC 256 KB build for the common "Proxmark3 Easy" board.</summary>
        public bool Easy256K { get; set; } = true;

        /// <summary>Last known usbipd bus id of the device.</summary>
        public string BusId { get; set; } = "";

        /// <summary>Windows folder dumps are copied to.</summary>
        public string DumpFolder { get; set; } =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Proxmark3 Dumps");

        /// <summary>Setup steps already completed, so the wizard can resume.</summary>
        public System.Collections.Generic.List<string> CompletedSteps { get; set; } =
            new System.Collections.Generic.List<string>();

        // ---------------------------------------------------------------- storage

        [JsonIgnore]
        public static string Folder
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "Proxmark3Gui");
            }
        }

        [JsonIgnore]
        public static string FilePath { get { return Path.Combine(Folder, "settings.json"); } }

        public static AppSettings Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    var s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath));
                    if (s != null) return s;
                }
            }
            catch { /* corrupt settings must never block startup */ }
            return new AppSettings();
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Folder);
                File.WriteAllText(FilePath,
                    JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch { }
        }

        public bool IsStepDone(string id) { return CompletedSteps.Contains(id); }

        public void MarkStepDone(string id)
        {
            if (!CompletedSteps.Contains(id)) CompletedSteps.Add(id);
            Save();
        }

        public void ResetSteps()
        {
            CompletedSteps.Clear();
            Save();
        }
    }

    public static class Defaults
    {
        /// <summary>
        /// GitHub owner of the Proxmark3 source the app clones from — a fork kept on this account
        /// so installs never depend on upstream staying available. Change the owner here (and in
        /// scripts/sync-mirrors.ps1) to point at a different fork.
        /// </summary>
        public const string MirrorOwner = "giulbaziani";

        /// <summary>The fork the app clones and builds from.</summary>
        public const string SourceRepoUrl = "https://github.com/" + MirrorOwner + "/proxmark3.git";

        /// <summary>Upstream, used only if the fork above cannot be reached.</summary>
        public const string FallbackRepoUrl = "https://github.com/RfidResearchGroup/proxmark3.git";

        public const string UsbIpdWingetId = "dorssel.usbipd-win";
        public const string UsbIpdReleasesUrl = "https://github.com/dorssel/usbipd-win/releases/latest";

        /// <summary>Ubuntu 24.04 WSL root filesystem, used when the app imports its own distro.</summary>
        public const string UbuntuRootfsUrl =
            "https://cloud-images.ubuntu.com/wsl/releases/24.04/current/ubuntu-noble-wsl-amd64-wsl.rootfs.tar.gz";
    }
}
