using System;
using NAudio.CoreAudioApi;

namespace MestEQ.Services
{
    /// <summary>
    /// Varsayılan çıkış cihazını, örnekleme hızını ve Windows ses seviyesini izler.
    /// Tüm COM çağrıları UI iş parçacığından yapılır; bildirimler sadece "değişti" sinyali verir.
    /// </summary>
    public sealed class AudioDeviceWatcher : IDisposable
    {
        private readonly MMDeviceEnumerator _enumerator = new();
        private MMDevice? _device;

        public string DeviceId { get; private set; } = "";
        public string DeviceName { get; private set; } = "Cihaz bulunamadı";
        public int SampleRate { get; private set; }
        public int BitDepth { get; private set; }
        public double VolumeDb { get; private set; } = double.NaN;
        public bool Muted { get; private set; }

        /// <summary>Cihaz değişti (UI iş parçacığından çağrılır).</summary>
        public event Action? DeviceChanged;
        /// <summary>Ses seviyesi değişti; arka plan iş parçacığından gelir.</summary>
        public event Action? VolumeSignal;

        public bool IsDawnPro =>
            DeviceName.Contains("Dawn", StringComparison.OrdinalIgnoreCase) ||
            DeviceName.Contains("MOONDROP", StringComparison.OrdinalIgnoreCase);

        /// <summary>Varsayılan cihaz değiştiyse yeniden bağlanır. Zamanlayıcıyla periyodik çağrılır.</summary>
        public void Refresh()
        {
            MMDevice? current;
            try { current = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia); }
            catch { current = null; }

            if (current == null)
            {
                if (_device != null) { Detach(); DeviceId = ""; DeviceName = "Cihaz bulunamadı"; SampleRate = 0; DeviceChanged?.Invoke(); }
                return;
            }

            if (_device != null && current.ID == DeviceId)
            {
                current.Dispose();
                return;
            }

            Detach();
            _device = current;
            DeviceId = current.ID;
            DeviceName = current.FriendlyName;
            try
            {
                using var client = current.AudioClient;
                SampleRate = client.MixFormat.SampleRate;
                BitDepth = client.MixFormat.BitsPerSample;
            }
            catch { SampleRate = 0; BitDepth = 0; }

            try { current.AudioEndpointVolume.OnVolumeNotification += OnVolume; } catch { }
            ReadVolume();
            DeviceChanged?.Invoke();
        }

        /// <summary>Ses seviyesini okur (UI iş parçacığından çağrılmalı).</summary>
        public void ReadVolume()
        {
            if (_device == null) { VolumeDb = double.NaN; return; }
            try
            {
                VolumeDb = _device.AudioEndpointVolume.MasterVolumeLevel;
                Muted = _device.AudioEndpointVolume.Mute;
            }
            catch { VolumeDb = double.NaN; }
        }

        private void OnVolume(AudioVolumeNotificationData data) => VolumeSignal?.Invoke();

        private void Detach()
        {
            if (_device == null) return;
            try { _device.AudioEndpointVolume.OnVolumeNotification -= OnVolume; } catch { }
            try { _device.Dispose(); } catch { }
            _device = null;
        }

        public void Dispose()
        {
            Detach();
            _enumerator.Dispose();
        }
    }
}
