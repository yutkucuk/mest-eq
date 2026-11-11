using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace MestEQ.Core
{
    public enum Layer { Base, Taste, Preset, Loudness }

    public sealed class LayeredFilter
    {
        public Layer Layer { get; }
        public EqFilter Filter { get; }
        public LayeredFilter(Layer layer, EqFilter filter) { Layer = layer; Filter = filter; }
    }

    /// <summary>
    /// Ses zincirinin tamamını tek yerde tarif eder:
    /// Preamp → Temel düzeltme → Zevk → Preset → Loudness → Denge → Crossfeed.
    /// </summary>
    public static class ConfigBuilder
    {
        private static readonly CultureInfo C = CultureInfo.InvariantCulture;

        public const double TasteBassFreq = 105, TasteWarmthFreq = 250, TastePresenceFreq = 3000, TasteAirFreq = 10000;

        public static List<EqFilter> TasteFilters(AppSettings s)
        {
            var list = new List<EqFilter>();
            if (!s.TasteEnabled) return list;
            if (Math.Abs(s.TasteBass) > 0.05) list.Add(new EqFilter(FilterKind.LowShelf, TasteBassFreq, s.TasteBass, 0.7));
            if (Math.Abs(s.TasteWarmth) > 0.05) list.Add(new EqFilter(FilterKind.Peak, TasteWarmthFreq, s.TasteWarmth, 0.8));
            if (Math.Abs(s.TastePresence) > 0.05) list.Add(new EqFilter(FilterKind.Peak, TastePresenceFreq, s.TastePresence, 1.0));
            if (Math.Abs(s.TasteAir) > 0.05) list.Add(new EqFilter(FilterKind.HighShelf, TasteAirFreq, s.TasteAir, 0.7));
            return list;
        }

        /// <summary>
        /// Eşit ses yüksekliği eğrilerinin basitleştirilmiş hali: referansın altındaki her dB kısma için
        /// basa ve (daha az) tize telafi. 0,5 dB adımlara yuvarlanır ki her ses tuşunda APO yeniden yüklenmesin.
        /// </summary>
        public static (double bass, double treble) LoudnessGains(AppSettings s, double volumeDb)
        {
            if (!s.LoudnessEnabled || double.IsNaN(volumeDb)) return (0, 0);
            double att = s.LoudnessReferenceDb - volumeDb;
            if (att <= 0) return (0, 0);
            double amt = Math.Clamp(s.LoudnessAmount, 0, 1.5);
            double bass = Math.Min(10, 0.4 * att * amt);
            double treble = Math.Min(4, 0.15 * att * amt);
            return (Math.Round(bass * 2) / 2, Math.Round(treble * 2) / 2);
        }

        public static List<EqFilter> LoudnessFilters(AppSettings s, double volumeDb)
        {
            var (bass, treble) = LoudnessGains(s, volumeDb);
            var list = new List<EqFilter>();
            if (bass > 0) list.Add(new EqFilter(FilterKind.LowShelf, 120, bass, 0.55));
            if (treble > 0) list.Add(new EqFilter(FilterKind.HighShelf, 9000, treble, 0.6));
            return list;
        }

        public static List<LayeredFilter> Collect(AppSettings s, Preset? preset, double volumeDb)
        {
            var all = new List<LayeredFilter>();
            if (s.BaseEnabled) all.AddRange(s.BaseFilters.Where(f => f.Enabled).Select(f => new LayeredFilter(Layer.Base, f)));
            all.AddRange(TasteFilters(s).Select(f => new LayeredFilter(Layer.Taste, f)));
            if (preset != null) all.AddRange(preset.Filters.Where(f => f.Enabled).Select(f => new LayeredFilter(Layer.Preset, f)));
            all.AddRange(LoudnessFilters(s, volumeDb).Select(f => new LayeredFilter(Layer.Loudness, f)));
            return all;
        }

        private static readonly double[] Grid = Biquad.LogGrid(600);

        public static double MaxBoostDb(IEnumerable<EqFilter> filters)
        {
            var r = Biquad.Response(filters, Grid);
            return r.Length == 0 ? 0 : r.Max();
        }

        /// <summary>Kulağın en hassas olduğu 100 Hz – 8 kHz bandında ortalama kazanç (A/B seviye eşleme için).</summary>
        public static double AverageLevelDb(IEnumerable<EqFilter> filters)
        {
            var r = Biquad.Response(filters, Grid);
            double sum = 0; int n = 0;
            for (int i = 0; i < Grid.Length; i++)
            {
                if (Grid[i] < 100 || Grid[i] > 8000) continue;
                sum += r[i]; n++;
            }
            return n == 0 ? 0 : sum / n;
        }

        /// <summary>Clipping'i önleyen preamp (her zaman ≤ 0).</summary>
        public static double SafePreamp(IEnumerable<EqFilter> filters, double safetyMarginDb)
        {
            double max = MaxBoostDb(filters);
            if (max <= 0.01) return 0;
            return -Math.Round(max + safetyMarginDb, 1);
        }

        public static string Render(AppSettings s, Preset? preset, double volumeDb, double? preampOverride = null, string? label = null)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# Mest EQ tarafından otomatik üretildi. Elle yapılan değişiklikler bir sonraki ayarda silinir.");
            sb.AppendLine("# " + (label ?? (preset != null ? "Preset: " + preset.Name : "Preset yok")));

            if (s.Bypass)
            {
                sb.AppendLine("# EQ kapalı (bypass)");
                return sb.ToString();
            }

            var layered = Collect(s, preset, volumeDb);
            var filters = layered.Select(l => l.Filter).ToList();
            double preamp = preampOverride ?? SafePreamp(filters, s.SafetyMarginDb);

            sb.AppendLine("Channel: all");
            sb.AppendLine($"Preamp: {preamp.ToString("0.0", C)} dB");

            WriteLayer(sb, layered, Layer.Base, "Temel düzeltme (kulaklık)");
            WriteLayer(sb, layered, Layer.Taste, "Zevk katmanı");
            WriteLayer(sb, layered, Layer.Preset, "Preset");
            WriteLayer(sb, layered, Layer.Loudness, "Loudness telafisi");

            double bal = Math.Clamp(s.Balance, -1, 1) * 3.0;
            if (Math.Abs(bal) >= 0.05)
            {
                sb.AppendLine("# Kanal dengesi");
                sb.AppendLine(bal > 0 ? "Channel: L" : "Channel: R");
                sb.AppendLine($"Preamp: {(-Math.Abs(bal)).ToString("0.0", C)} dB");
                sb.AppendLine("Channel: all");
            }

            if (s.CrossfeedEnabled)
            {
                // Mid/Side tabanlı crossfeed: düşük frekanslarda stereo genişliği daraltır,
                // sert sağ/sol ayrımını hoparlör dinliyormuş gibi birleştirir. Her Copy satırı
                // bir öncekinin sonucunu kullanır; sıralama bilerek böyle.
                double side = -(1.5 + 7.5 * Math.Clamp(s.CrossfeedAmount, 0, 1));
                sb.AppendLine("# Crossfeed");
                sb.AppendLine("Copy: L=0.5*L+0.5*R");          // L = Mid
                sb.AppendLine("Copy: R=R+-1.0*L");             // R = Side
                sb.AppendLine("Channel: R");
                sb.AppendLine($"Filter: ON LSC Fc 700 Hz Gain {side.ToString("0.00", C)} dB Q 0.500");
                sb.AppendLine("Channel: all");
                sb.AppendLine("Copy: R=L+R");                  // R = Mid + Side = sağ
                sb.AppendLine("Copy: L=2.0*L+-1.0*R");         // L = 2*Mid - sağ = sol
            }

            return sb.ToString();
        }

        private static void WriteLayer(StringBuilder sb, List<LayeredFilter> all, Layer layer, string title)
        {
            var items = all.Where(l => l.Layer == layer).ToList();
            if (items.Count == 0) return;
            sb.AppendLine("# " + title);
            foreach (var l in items) sb.AppendLine(l.Filter.ToApoLine());
        }
    }
}
