using System;
using System.IO;
using System.Linq;
using MestEQ.Core;

static class P
{
    static int fails = 0;
    static void Check(bool ok, string what) { Console.WriteLine((ok ? "OK   " : "FAIL ") + what); if (!ok) fails++; }

    static int Main()
    {
        // 1) Peaking filtre merkez frekansta tam kazanç
        var pk = Biquad.Design(FilterKind.Peak, 1000, 6, 1.0);
        Check(Math.Abs(Biquad.MagnitudeDb(pk, 1000) - 6) < 0.01, "PK 1k +6 dB merkezde 6 dB: " + Biquad.MagnitudeDb(pk, 1000).ToString("0.000"));
        Check(Math.Abs(Biquad.MagnitudeDb(pk, 20)) < 0.1, "PK 20 Hz'de ~0 dB");

        // 2) Raflar
        var ls = Biquad.Design(FilterKind.LowShelf, 100, 5, 0.707);
        Check(Math.Abs(Biquad.MagnitudeDb(ls, 20) - 5) < 0.3, "LS 20 Hz ≈ +5: " + Biquad.MagnitudeDb(ls, 20).ToString("0.00"));
        Check(Math.Abs(Biquad.MagnitudeDb(ls, 100) - 2.5) < 0.05, "LS fc'de yarı kazanç: " + Biquad.MagnitudeDb(ls, 100).ToString("0.00"));
        var hs = Biquad.Design(FilterKind.HighShelf, 8000, -4, 0.707);
        Check(Math.Abs(Biquad.MagnitudeDb(hs, 20000) + 4) < 0.4, "HS 20k ≈ -4: " + Biquad.MagnitudeDb(hs, 20000).ToString("0.00"));

        // 3) İçe aktarma (AutoEQ ParametricEQ.txt formatı + virgüllü sayılar + BW)
        string autoeq = "Preamp: -6.4 dB\nFilter 1: ON LSC Fc 105 Hz Gain 6.0 dB Q 0.70\nFilter 2: ON PK Fc 2237 Hz Gain -4.6 dB Q 1.94\nFilter 3: ON HSC Fc 10000 Hz Gain -2,5 dB Q 0,70\nFilter 4: ON PEQ Fc 100 Hz Gain 1.0 dB BW Oct 1.0\nFilter 5: ON NO Fc 50 Hz\n# yorum";
        var imp = ApoImporter.Parse(autoeq, out int skipped);
        Check(imp.Count == 4, "4 filtre içe aktarıldı: " + imp.Count);
        Check(skipped == 1, "Desteklenmeyen notch atlandı");
        Check(imp[0].Kind == FilterKind.LowShelf && imp[2].Kind == FilterKind.HighShelf && Math.Abs(imp[2].Gain + 2.5) < 1e-9, "Tip ve virgül ayrıştırma");
        Check(Math.Abs(imp[3].Q - 1.4142) < 0.01, "1 oktav BW → Q 1.414: " + imp[3].Q.ToString("0.0000"));

        // 4) Yapılandırma metni
        var s = new AppSettings();
        foreach (var f in DefaultPresets.AltruvaEstimatedBase()) s.BaseFilters.Add(f);
        s.TasteBass = 2; s.TasteAir = 1;
        var rock = DefaultPresets.All().First(p => p.Name == "Rock");
        string cfg = ConfigBuilder.Render(s, rock, -10);
        Console.WriteLine("----- örnek config -----\n" + cfg + "------------------------");
        Check(cfg.Contains("Preamp: -"), "Preamp negatif");
        Check(cfg.Contains("Filter: ON LSC Fc 55 Hz Gain 2.00 dB Q 0.700"), "APO satır formatı");
        double pre = ConfigBuilder.SafePreamp(ConfigBuilder.Collect(s, rock, -10).Select(l => l.Filter), 0.5);
        double max = ConfigBuilder.MaxBoostDb(ConfigBuilder.Collect(s, rock, -10).Select(l => l.Filter));
        Check(pre <= -(max) && pre >= -(max + 0.6), $"Preamp tepe kazancı karşılıyor (max {max:0.00}, preamp {pre:0.0})");

        // 5) Loudness
        s.LoudnessEnabled = true; s.LoudnessReferenceDb = -6;
        var (b0, t0) = ConfigBuilder.LoudnessGains(s, -6);
        var (b1, t1) = ConfigBuilder.LoudnessGains(s, -26);
        Check(b0 == 0 && t0 == 0, "Referansta loudness yok");
        Check(b1 == 8 && t1 == 3, $"20 dB kısıkta bas +{b1}, tiz +{t1}");

        // 6) Crossfeed Copy zinciri: yan kanal filtresi 0 dB iken giriş = çıkış olmalı
        double L = 0.8, R = -0.3;
        L = 0.5 * L + 0.5 * R;      // Copy: L=0.5*L+0.5*R
        R = R + -1.0 * L;           // Copy: R=R+-1.0*L
        R = L + R;                  // Copy: R=L+R
        L = 2.0 * L + -1.0 * R;     // Copy: L=2.0*L+-1.0*R
        Check(Math.Abs(L - 0.8) < 1e-12 && Math.Abs(R + 0.3) < 1e-12, $"Crossfeed M/S zinciri kayıpsız geri dönüyor (L {L}, R {R})");
        // Yan kanal tamamen sıfırlanırsa iki kanal mono olmalı
        L = 0.8; R = -0.3; L = 0.5 * L + 0.5 * R; R = R + -1.0 * L; R *= 0; R = L + R; L = 2.0 * L + -1.0 * R;
        Check(Math.Abs(L - R) < 1e-12, "Yan kanal 0 → mono");

        // 7) Eşleştirme önceliği
        var presets = DefaultPresets.All().ToList();
        var metal = presets.First(p => p.Name == "Metal");
        var jazz = presets.First(p => p.Name == "Caz");
        metal.Artists.Add("gojira");
        jazz.Tracks.Add("gojira|another world");
        var m1 = PresetMatcher.Match(presets, new NowPlaying("Stranded", "Gojira", "Magma", "Spotify"), DefaultPresets.NeutralId);
        var m2 = PresetMatcher.Match(presets, new NowPlaying("Another World", "Gojira", "Fortitude", "Spotify"), DefaultPresets.NeutralId);
        var m3 = PresetMatcher.Match(presets, new NowPlaying("So What", "Miles Davis", "Kind of Blue", "Spotify"), DefaultPresets.NeutralId);
        var m4 = PresetMatcher.Match(presets, new NowPlaying("Amazonia", "Gojira, Someone", "Fortitude", "Spotify"), DefaultPresets.NeutralId);
        Check(m1.preset == metal && m1.level == PresetMatcher.MatchLevel.Artist, "Sanatçı eşleşmesi");
        Check(m2.preset == jazz && m2.level == PresetMatcher.MatchLevel.Track, "Şarkı, sanatçıyı ezer");
        Check(m3.level == PresetMatcher.MatchLevel.Default, "Eşleşme yoksa varsayılan");
        Check(m4.preset == metal, "Çoklu sanatçı ayrıştırma");

        // 8) Kayıt / yükleme
        var dir = Path.Combine(Path.GetTempPath(), "mest-test-" + Guid.NewGuid().ToString("N"));
        var st = new Storage(dir);
        var loaded = st.LoadPresets();
        Check(loaded.Count == 6, "6 yerleşik preset diske yazıldı: " + loaded.Count);
        var settings = st.LoadSettings();
        Check(settings.BaseFilters.Count == 1 && settings.DefaultPresetId == DefaultPresets.NeutralId, "Varsayılan ayarlar");
        settings.TasteBass = 3.5; st.SaveSettings(settings);
        Check(Math.Abs(st.LoadSettings().TasteBass - 3.5) < 1e-9, "Ayarlar gidiş-dönüş");
        var mine = loaded.First(p => p.Name == "Metal").Clone("Benim metalim"); mine.Artists.Add("meshuggah"); st.SavePreset(mine);
        var again = st.LoadPresets().First(p => p.Name == "Benim metalim");
        Check(again.Filters.Count == 4 && again.Artists[0] == "meshuggah" && again.Filters[0].Kind == FilterKind.Peak, "Preset gidiş-dönüş (enum string)");
        Directory.Delete(dir, true);

        Console.WriteLine(fails == 0 ? "\nTÜM TESTLER GEÇTİ" : $"\n{fails} TEST BAŞARISIZ");
        return fails;
    }
}
