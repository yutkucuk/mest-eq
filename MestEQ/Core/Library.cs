using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MestEQ.Core
{
    /// <summary>
    /// AutoEQ "ParametricEQ.txt", squig.link "Export EQ" ve Equalizer APO formatındaki
    /// "Filter: ON PK Fc 100 Hz Gain -3 dB Q 1.41" satırlarını okur.
    /// </summary>
    public static class ApoImporter
    {
        private static readonly Regex Line = new(
            @"Filter\s*\d*\s*:\s*ON\s+(?<type>PK|PEQ|Modal|LSC|HSC|LS|HS|LSQ|HSQ)(?:\s+(?<slope>[\d.]+)\s*dB)?\s+Fc\s+(?<fc>[\d.,]+)\s*Hz\s+Gain\s+(?<gain>[-+]?[\d.,]+)\s*dB(?:\s+Q\s+(?<q>[\d.,]+))?(?:\s+BW\s+Oct\s+(?<bw>[\d.,]+))?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static List<EqFilter> Parse(string text, out int skipped)
        {
            var result = new List<EqFilter>();
            skipped = 0;
            foreach (var raw in text.Split('\n'))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                if (!line.StartsWith("Filter", StringComparison.OrdinalIgnoreCase)) continue;
                var m = Line.Match(line);
                if (!m.Success) { skipped++; continue; }

                string type = m.Groups["type"].Value.ToUpperInvariant();
                var kind = type switch
                {
                    "LSC" or "LS" or "LSQ" => FilterKind.LowShelf,
                    "HSC" or "HS" or "HSQ" => FilterKind.HighShelf,
                    _ => FilterKind.Peak
                };
                double fc = Num(m.Groups["fc"].Value);
                double gain = Num(m.Groups["gain"].Value);
                double q;
                if (m.Groups["q"].Success) q = Num(m.Groups["q"].Value);
                else if (m.Groups["bw"].Success) q = QFromBandwidth(Num(m.Groups["bw"].Value));
                else q = kind == FilterKind.Peak ? 1.0 : 0.707;
                result.Add(new EqFilter(kind, fc, gain, q));
            }
            return result;
        }

        private static double Num(string s) => double.Parse(s.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture);

        public static double QFromBandwidth(double octaves)
        {
            double p = Math.Pow(2, octaves);
            return Math.Sqrt(p) / (p - 1);
        }
    }

    public static class PresetMatcher
    {
        public static string Norm(string s) => (s ?? "").Trim().ToLowerInvariant();

        /// <summary>Spotify birden fazla sanatçıyı "A, B" şeklinde verebilir.</summary>
        public static IEnumerable<string> SplitArtists(string artist)
        {
            var full = Norm(artist);
            if (full.Length == 0) yield break;
            yield return full;
            foreach (var part in full.Split(new[] { ", ", " & ", " feat. ", " ft. ", " x " }, StringSplitOptions.RemoveEmptyEntries))
            {
                var p = part.Trim();
                if (p.Length > 0 && p != full) yield return p;
            }
        }

        public static string TrackKey(NowPlaying np) => Norm(FirstArtist(np.Artist)) + "|" + Norm(np.Title);
        public static string AlbumKey(NowPlaying np) => Norm(FirstArtist(np.Artist)) + "|" + Norm(np.Album);
        public static string ArtistKey(NowPlaying np) => Norm(FirstArtist(np.Artist));

        private static string FirstArtist(string artist)
        {
            var a = (artist ?? "").Trim();
            int i = a.IndexOf(", ", StringComparison.Ordinal);
            return i > 0 ? a.Substring(0, i) : a;
        }

        public enum MatchLevel { None, Default, Artist, Album, Track }

        /// <summary>Öncelik: şarkı → albüm → sanatçı → varsayılan.</summary>
        public static (Preset? preset, MatchLevel level) Match(IEnumerable<Preset> presets, NowPlaying np, string? defaultId)
        {
            var list = presets.ToList();
            if (!np.IsEmpty)
            {
                string tk = TrackKey(np);
                var t = list.FirstOrDefault(p => p.Tracks.Contains(tk));
                if (t != null) return (t, MatchLevel.Track);

                if (!string.IsNullOrWhiteSpace(np.Album))
                {
                    string ak = AlbumKey(np);
                    var a = list.FirstOrDefault(p => p.Albums.Contains(ak));
                    if (a != null) return (a, MatchLevel.Album);
                }

                var artists = SplitArtists(np.Artist).ToList();
                foreach (var artist in artists)
                {
                    var s = list.FirstOrDefault(p => p.Artists.Contains(artist));
                    if (s != null) return (s, MatchLevel.Artist);
                }
            }
            var d = list.FirstOrDefault(p => p.Id == defaultId);
            return d != null ? (d, MatchLevel.Default) : (null, MatchLevel.None);
        }
    }

    /// <summary>%APPDATA%\MestEQ altında JSON kayıtları.</summary>
    public sealed class Storage
    {
        private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
        public string Root { get; }
        public string PresetDir => Path.Combine(Root, "presets");
        public string SettingsPath => Path.Combine(Root, "settings.json");

        public Storage(string root)
        {
            Root = root;
            Directory.CreateDirectory(PresetDir);
        }

        public AppSettings LoadSettings()
        {
            try
            {
                if (File.Exists(SettingsPath))
                    return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath), Json) ?? NewSettings();
            }
            catch { /* bozuk dosya: varsayılanlara dön */ }
            return NewSettings();
        }

        private static AppSettings NewSettings()
        {
            var s = new AppSettings();
            foreach (var f in DefaultPresets.AltruvaEstimatedBase()) s.BaseFilters.Add(f);
            s.DefaultPresetId = DefaultPresets.NeutralId;
            s.ActivePresetId = DefaultPresets.NeutralId;
            return s;
        }

        public void SaveSettings(AppSettings s) => AtomicWrite(SettingsPath, JsonSerializer.Serialize(s, Json));

        public List<Preset> LoadPresets()
        {
            var list = new List<Preset>();
            foreach (var file in Directory.GetFiles(PresetDir, "*.json"))
            {
                try
                {
                    var p = JsonSerializer.Deserialize<Preset>(File.ReadAllText(file), Json);
                    if (p != null) list.Add(p);
                }
                catch { /* okunamayan preset atlanır */ }
            }
            // Yerleşik presetler eksikse (ilk açılış ya da silinmişse) geri eklenir.
            foreach (var d in DefaultPresets.All())
            {
                if (list.All(p => p.Id != d.Id)) { list.Add(d); SavePreset(d); }
            }
            return list.OrderBy(p => GroupOrder(p.Group)).ThenBy(p => p.Name, StringComparer.CurrentCulture).ToList();
        }

        public static int GroupOrder(string g) => g switch
        {
            "Türler" => 0,
            "Benim presetlerim" => 1,
            _ => 2
        };

        public void SavePreset(Preset p) => AtomicWrite(Path.Combine(PresetDir, p.Id + ".json"), JsonSerializer.Serialize(p, Json));

        public void DeletePreset(Preset p)
        {
            var path = Path.Combine(PresetDir, p.Id + ".json");
            if (File.Exists(path)) File.Delete(path);
        }

        public static void AtomicWrite(string path, string content)
        {
            var dir = Path.GetDirectoryName(path)!;
            Directory.CreateDirectory(dir);
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, content, new System.Text.UTF8Encoding(false));
            if (File.Exists(path)) File.Replace(tmp, path, null);
            else File.Move(tmp, path);
        }
    }
}
