using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Microsoft.Win32;
using MestEQ.Core;

namespace MestEQ.Services
{
    public enum ApoDeviceState { Unknown, Active, NotActive }

    /// <summary>
    /// Arka plandaki Equalizer APO motoruyla konuşur. Kullanıcının config.txt'sine tek bir
    /// "Include: mest.txt" satırı koyar; tüm ayarlar mest.txt dosyasına yazılır.
    /// </summary>
    public sealed class ApoService
    {
        /// <summary>Equalizer APO'nun ses efekt sınıf kimliği (cihazın FxProperties kaydında görünür).</summary>
        public const string ApoClsid = "{EACD2258-FCAC-4FF4-B36D-419E924A6D79}";
        public const string MestFileName = "mest.txt";

        public string? InstallPath { get; private set; }
        public string? ConfigPath { get; private set; }
        public bool IsInstalled => ConfigPath != null && Directory.Exists(ConfigPath);
        public string? LastError { get; private set; }

        public ApoService() => Detect();

        public void Detect()
        {
            try
            {
                using var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
                using var key = hklm.OpenSubKey(@"SOFTWARE\EqualizerAPO");
                InstallPath = key?.GetValue("InstallPath") as string;
                ConfigPath = key?.GetValue("ConfigPath") as string;
            }
            catch { /* kayıt okunamadı: varsayılan yola bakılır */ }

            var defaultDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "EqualizerAPO");
            if (string.IsNullOrEmpty(InstallPath) && Directory.Exists(defaultDir)) InstallPath = defaultDir;
            if (string.IsNullOrEmpty(ConfigPath) && InstallPath != null) ConfigPath = Path.Combine(InstallPath, "config");
        }

        private string MainConfig => Path.Combine(ConfigPath!, "config.txt");
        private string MestConfig => Path.Combine(ConfigPath!, MestFileName);

        /// <summary>config.txt'nin mest.txt'yi yüklediğinden emin olur. Eski içerik bir kez yedeklenir.</summary>
        public bool EnsureIncluded()
        {
            if (!IsInstalled) { LastError = "Equalizer APO bulunamadı."; return false; }
            try
            {
                string current = File.Exists(MainConfig) ? File.ReadAllText(MainConfig) : "";
                bool included = current.Split('\n').Any(l => l.Trim().Equals("Include: " + MestFileName, StringComparison.OrdinalIgnoreCase));
                if (included) return true;

                var backup = Path.Combine(ConfigPath!, "config.mest-yedek.txt");
                if (current.Length > 0 && !File.Exists(backup)) File.WriteAllText(backup, current);

                File.WriteAllText(MainConfig,
                    "# Bu dosya Mest EQ tarafından yönetiliyor. Önceki içerik: config.mest-yedek.txt\r\n" +
                    "Include: " + MestFileName + "\r\n");
                LastError = null;
                return true;
            }
            catch (Exception ex)
            {
                LastError = "config.txt yazılamadı: " + ex.Message;
                return false;
            }
        }

        public bool Write(string text)
        {
            if (!IsInstalled) { LastError = "Equalizer APO bulunamadı."; return false; }
            try
            {
                Storage.AtomicWrite(MestConfig, text.Replace("\r\n", "\n").Replace("\n", "\r\n"));
                LastError = null;
                return true;
            }
            catch
            {
                try
                {
                    File.WriteAllText(MestConfig, text);
                    LastError = null;
                    return true;
                }
                catch (Exception ex)
                {
                    LastError = "Ayar dosyası yazılamadı: " + ex.Message;
                    return false;
                }
            }
        }

        /// <summary>APO'nun verilen ses cihazına kurulu olup olmadığını kayıt defterinden okur.</summary>
        public ApoDeviceState CheckDevice(string? mmDeviceId)
        {
            if (string.IsNullOrEmpty(mmDeviceId)) return ApoDeviceState.Unknown;
            try
            {
                // MMDevice kimliği: "{0.0.0.00000000}.{uç-nokta-guid}"
                int dot = mmDeviceId.LastIndexOf("}.{", StringComparison.Ordinal);
                string guid = dot >= 0 ? mmDeviceId.Substring(dot + 2) : mmDeviceId;
                using var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
                using var fx = hklm.OpenSubKey($@"SOFTWARE\Microsoft\Windows\CurrentVersion\MMDevices\Audio\Render\{guid}\FxProperties");
                if (fx == null) return ApoDeviceState.NotActive;
                foreach (var name in fx.GetValueNames())
                {
                    var v = fx.GetValue(name);
                    if (v is string s && s.Contains(ApoClsid, StringComparison.OrdinalIgnoreCase)) return ApoDeviceState.Active;
                    if (v is string[] arr && arr.Any(x => x.Contains(ApoClsid, StringComparison.OrdinalIgnoreCase))) return ApoDeviceState.Active;
                }
                return ApoDeviceState.NotActive;
            }
            catch
            {
                return ApoDeviceState.Unknown;
            }
        }

        /// <summary>Cihaz seçim penceresini açar (yönetici izni ister).</summary>
        public bool OpenDeviceSelector()
        {
            if (InstallPath == null) return false;
            foreach (var exe in new[] { "DeviceSelector.exe", "Configurator.exe" })
            {
                var path = Path.Combine(InstallPath, exe);
                if (!File.Exists(path)) continue;
                try
                {
                    Process.Start(new ProcessStartInfo(path) { UseShellExecute = true, Verb = "runas" });
                    return true;
                }
                catch { return false; }
            }
            return false;
        }
    }
}
