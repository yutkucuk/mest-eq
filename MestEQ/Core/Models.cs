using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace MestEQ.Core
{
    /// <summary>
    /// Bir ses imzası. Bir tür, sanatçı, albüm ya da tek bir şarkı için olabilir;
    /// hangi müziğe uygulanacağını Artists / Albums / Tracks listeleri belirler.
    /// </summary>
    public sealed class Preset
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Name { get; set; } = "Yeni preset";
        public string Group { get; set; } = "Benim presetlerim";
        public string Notes { get; set; } = "";
        public ObservableCollection<EqFilter> Filters { get; set; } = new();

        /// <summary>Sanatçı adı (küçük harf).</summary>
        public List<string> Artists { get; set; } = new();
        /// <summary>"sanatçı|albüm" (küçük harf).</summary>
        public List<string> Albums { get; set; } = new();
        /// <summary>"sanatçı|şarkı" (küçük harf).</summary>
        public List<string> Tracks { get; set; } = new();

        public bool BuiltIn { get; set; }

        public Preset Clone(string newName)
        {
            return new Preset
            {
                Name = newName,
                Group = "Benim presetlerim",
                Notes = Notes,
                Filters = new ObservableCollection<EqFilter>(Filters.Select(f => f.Clone()))
            };
        }

        public int BindingCount => Artists.Count + Albums.Count + Tracks.Count;
    }

    public sealed class AppSettings
    {
        // Temel düzeltme (kulaklık)
        public bool BaseEnabled { get; set; } = true;
        public string BaseSource { get; set; } = "Tahmini başlangıç eğrisi (ölçüm içe aktarılmadı)";
        public ObservableCollection<EqFilter> BaseFilters { get; set; } = new();

        // Zevk katmanı
        public bool TasteEnabled { get; set; } = true;
        public double TasteBass { get; set; }
        public double TasteWarmth { get; set; }
        public double TastePresence { get; set; }
        public double TasteAir { get; set; }

        // Loudness telafisi
        public bool LoudnessEnabled { get; set; }
        /// <summary>Telafinin sıfır olduğu Windows ses seviyesi (dB).</summary>
        public double LoudnessReferenceDb { get; set; } = -6;
        /// <summary>0..1.5 arası etki çarpanı.</summary>
        public double LoudnessAmount { get; set; } = 1.0;

        // Crossfeed
        public bool CrossfeedEnabled { get; set; }
        /// <summary>0..1</summary>
        public double CrossfeedAmount { get; set; } = 0.5;

        /// <summary>-1 (sol) .. +1 (sağ); dB cinsinden en fazla 3 dB.</summary>
        public double Balance { get; set; }

        public bool AutoSwitch { get; set; } = true;
        public string? DefaultPresetId { get; set; }
        public string? ActivePresetId { get; set; }
        public string? CompareBPresetId { get; set; }
        public bool Bypass { get; set; }
        public double SafetyMarginDb { get; set; } = 0.5;
        public bool CloseToTray { get; set; } = true;
        public bool ConfigInitialized { get; set; }
    }

    public sealed record NowPlaying(string Title, string Artist, string Album, string Source)
    {
        public static readonly NowPlaying Empty = new("", "", "", "");
        public bool IsEmpty => string.IsNullOrWhiteSpace(Title) && string.IsNullOrWhiteSpace(Artist);
    }
}
