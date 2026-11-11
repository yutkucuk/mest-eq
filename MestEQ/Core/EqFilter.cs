using System;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace MestEQ.Core
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum FilterKind
    {
        Peak,
        LowShelf,
        HighShelf
    }

    /// <summary>Tek bir parametrik EQ filtresi (Equalizer APO ile birebir uyumlu).</summary>
    public sealed class EqFilter : INotifyPropertyChanged
    {
        private FilterKind _kind = FilterKind.Peak;
        private double _freq = 1000;
        private double _gain;
        private double _q = 1.0;
        private bool _enabled = true;

        public FilterKind Kind { get => _kind; set { if (_kind != value) { _kind = value; Changed(); Changed(nameof(KindLabel)); } } }
        public double Freq { get => _freq; set { var v = Math.Clamp(value, 15, 22000); if (Math.Abs(_freq - v) > 1e-9) { _freq = v; Changed(); Changed(nameof(FreqLabel)); } } }
        public double Gain { get => _gain; set { var v = Math.Clamp(value, -24, 24); if (Math.Abs(_gain - v) > 1e-9) { _gain = v; Changed(); Changed(nameof(GainLabel)); } } }
        public double Q { get => _q; set { var v = Math.Clamp(value, 0.1, 20); if (Math.Abs(_q - v) > 1e-9) { _q = v; Changed(); Changed(nameof(QLabel)); } } }
        public bool Enabled { get => _enabled; set { if (_enabled != value) { _enabled = value; Changed(); } } }

        [JsonIgnore] public string KindLabel => Kind switch
        {
            FilterKind.LowShelf => "Alçak raf",
            FilterKind.HighShelf => "Yüksek raf",
            _ => "Tepe"
        };
        [JsonIgnore] public string FreqLabel => Freq >= 1000 ? (Freq / 1000).ToString("0.##", CultureInfo.InvariantCulture) + " kHz" : Freq.ToString("0", CultureInfo.InvariantCulture) + " Hz";
        [JsonIgnore] public string GainLabel => (Gain > 0 ? "+" : "") + Gain.ToString("0.0", CultureInfo.InvariantCulture) + " dB";
        [JsonIgnore] public string QLabel => "Q " + Q.ToString("0.00", CultureInfo.InvariantCulture);

        public EqFilter() { }

        public EqFilter(FilterKind kind, double freq, double gain, double q)
        {
            _kind = kind; _freq = freq; _gain = gain; _q = q;
        }

        public EqFilter Clone() => new EqFilter(Kind, Freq, Gain, Q) { Enabled = Enabled };

        public void CycleKind()
        {
            Kind = Kind switch
            {
                FilterKind.Peak => FilterKind.LowShelf,
                FilterKind.LowShelf => FilterKind.HighShelf,
                _ => FilterKind.Peak
            };
        }

        /// <summary>Equalizer APO satırı. Raflar Q destekli LSC/HSC olarak yazılır (APO 1.2.1+).</summary>
        public string ToApoLine()
        {
            string type = Kind switch
            {
                FilterKind.LowShelf => "LSC",
                FilterKind.HighShelf => "HSC",
                _ => "PK"
            };
            var c = CultureInfo.InvariantCulture;
            return $"Filter: ON {type} Fc {Freq.ToString("0.##", c)} Hz Gain {Gain.ToString("0.00", c)} dB Q {Q.ToString("0.000", c)}";
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
