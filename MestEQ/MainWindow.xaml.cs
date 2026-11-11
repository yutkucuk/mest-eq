using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using MestEQ.Core;
using MestEQ.Services;

namespace MestEQ
{
    /// <summary>Metin kutularında hem "1.5" hem "1,5" kabul eder.</summary>
    public sealed class NumberConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
            value is double d ? d.ToString("0.##", CultureInfo.InvariantCulture) : "";

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var s = (value as string ?? "").Trim().Replace(',', '.');
            return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : Binding.DoNothing;
        }
    }

    public sealed class BindingItem
    {
        public string Kind { get; }
        public string Key { get; }
        public string Label { get; }
        public BindingItem(string kind, string key)
        {
            Kind = kind;
            Key = key;
            string shown = key.Replace("|", ", ");
            Label = kind switch
            {
                "track" => "Şarkı: " + shown,
                "album" => "Albüm: " + shown,
                _ => "Sanatçı: " + shown
            };
        }
    }

    public partial class MainWindow : Window
    {
        private const string FlatId = "__flat";

        private readonly Storage _storage;
        private readonly AppSettings _s;
        private readonly List<Preset> _presets;
        private ListCollectionView? _view;
        private readonly ApoService _apo = new();
        private readonly AudioDeviceWatcher _audio = new();
        private readonly MediaWatcher _media = new();

        private Preset? _active;
        private NowPlaying _now = NowPlaying.Empty;
        private bool _loading;
        private bool _suppressSelection;
        private bool _initialized;
        private bool _needsListRefresh;
        private bool _toldAboutTray;
        private string _lastWritten = "";
        private (double, double) _lastLoudness = (-1, -1);

        private readonly DispatcherTimer _applyTimer = new() { Interval = TimeSpan.FromMilliseconds(70) };
        private readonly DispatcherTimer _saveTimer = new() { Interval = TimeSpan.FromMilliseconds(800) };
        private readonly DispatcherTimer _deviceTimer = new() { Interval = TimeSpan.FromSeconds(3) };
        private readonly DispatcherTimer _volumeTimer = new() { Interval = TimeSpan.FromMilliseconds(180) };

        public event Action<bool>? BypassChanged;

        public MainWindow()
        {
            InitializeComponent();
            var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MestEQ");
            _storage = new Storage(root);
            _s = _storage.LoadSettings();
            _presets = _storage.LoadPresets();

            _applyTimer.Tick += (_, _) => { _applyTimer.Stop(); ApplyNow(); };
            _saveTimer.Tick += (_, _) => { _saveTimer.Stop(); SaveAll(); };
            _deviceTimer.Tick += (_, _) => { _audio.Refresh(); UpdateStatus(); };
            _volumeTimer.Tick += (_, _) => { _volumeTimer.Stop(); OnVolume(); };

            Graph.FiltersEdited += () => { RedrawOnly(); ScheduleApply(true); };
            Graph.AddFilterRequested += (f, g) => AddFilter(new EqFilter(FilterKind.Peak, Math.Round(f), g, 1.4));
            Graph.RemoveFilterRequested += f => _active?.Filters.Remove(f);

            Closing += OnClosing;
        }

        // ================== Başlatma ==================

        public void Initialize()
        {
            if (_initialized) return;
            _initialized = true;

            _view = new ListCollectionView(_presets);
            _view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(Preset.Group)));
            PresetList.ItemsSource = _view;

            LoadLayerControls();

            _apo.Detect();
            if (_apo.IsInstalled && !_apo.EnsureIncluded()) SetStatus(_apo.LastError ?? "");

            _audio.VolumeSignal += () => Dispatcher.BeginInvoke(new Action(() => { _volumeTimer.Stop(); _volumeTimer.Start(); }));
            _audio.DeviceChanged += () => { UpdateStatus(); ApplyNow(); };
            _audio.Refresh();
            _deviceTimer.Start();

            _media.Changed += np => Dispatcher.BeginInvoke(new Action(() => OnNowPlaying(np)));
            _ = _media.StartAsync();

            var start = _presets.FirstOrDefault(p => p.Id == _s.ActivePresetId)
                        ?? _presets.FirstOrDefault(p => p.Id == _s.DefaultPresetId)
                        ?? _presets.First();
            SetActive(start, "son kullanılan", select: true);
            UpdateNowPlayingUi();
            UpdateStatus();
            UpdateAbInfo();
        }

        private void LoadLayerControls()
        {
            _loading = true;
            foreach (var sl in new[] { BassSl, WarmSl, PresSl, AirSl }) { sl.TickFrequency = 0.5; sl.IsSnapToTickEnabled = true; }
            BalSl.TickFrequency = 0.05; BalSl.IsSnapToTickEnabled = true;

            BaseChk.IsChecked = _s.BaseEnabled;
            TasteChk.IsChecked = _s.TasteEnabled;
            BassSl.Value = _s.TasteBass;
            WarmSl.Value = _s.TasteWarmth;
            PresSl.Value = _s.TastePresence;
            AirSl.Value = _s.TasteAir;
            LoudChk.IsChecked = _s.LoudnessEnabled;
            LoudSl.Value = _s.LoudnessAmount;
            XfeedChk.IsChecked = _s.CrossfeedEnabled;
            XfeedSl.Value = _s.CrossfeedAmount;
            BalSl.Value = _s.Balance;
            EqOnChk.IsChecked = !_s.Bypass;
            AutoChk.IsChecked = _s.AutoSwitch;
            _loading = false;
            UpdateLayerLabels();
        }

        private void UpdateLayerLabels()
        {
            BassVal.Text = Db(BassSl.Value);
            WarmVal.Text = Db(WarmSl.Value);
            PresVal.Text = Db(PresSl.Value);
            AirVal.Text = Db(AirSl.Value);
            double bal = BalSl.Value * 3;
            BalVal.Text = Math.Abs(bal) < 0.05 ? "ortada" : (bal > 0 ? "sağ " : "sol ") + Math.Abs(bal).ToString("0.0", CultureInfo.InvariantCulture) + " dB";
            BaseSourceText.Text = _s.BaseSource;
            BaseCountText.Text = _s.BaseFilters.Count + " filtre";
        }

        private static string Db(double v) => (v > 0 ? "+" : "") + v.ToString("0.0", CultureInfo.InvariantCulture);

        // ================== Aktif preset ==================

        private void SetActive(Preset p, string why, bool select)
        {
            if (_active != null && !ReferenceEquals(_active, p)) Hook(_active, false);
            bool changed = !ReferenceEquals(_active, p);
            _active = p;
            if (changed) Hook(p, true);
            _s.ActivePresetId = p.Id;

            _loading = true;
            PresetName.Text = p.Name;
            PresetNotes.Text = p.Notes;
            _loading = false;
            FilterList.ItemsSource = p.Filters;
            RefreshBindings();
            DeleteBtn.IsEnabled = !p.BuiltIn;
            AppliedName.Text = p.Name;
            AppliedWhy.Text = why;

            if (select)
            {
                _suppressSelection = true;
                PresetList.SelectedItem = p;
                PresetList.ScrollIntoView(p);
                _suppressSelection = false;
            }
            UpdateDefaultButton();
            ApplyNow();
            if (_initialized) UpdateAbInfo();
            _saveTimer.Stop(); _saveTimer.Start();
        }

        private void Hook(Preset p, bool on)
        {
            if (on)
            {
                p.Filters.CollectionChanged += Filters_CollectionChanged;
                foreach (var f in p.Filters) f.PropertyChanged += Filter_PropertyChanged;
            }
            else
            {
                p.Filters.CollectionChanged -= Filters_CollectionChanged;
                foreach (var f in p.Filters) f.PropertyChanged -= Filter_PropertyChanged;
            }
        }

        private void Filters_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.OldItems != null) foreach (EqFilter f in e.OldItems) f.PropertyChanged -= Filter_PropertyChanged;
            if (e.NewItems != null) foreach (EqFilter f in e.NewItems) f.PropertyChanged += Filter_PropertyChanged;
            if (e.Action == NotifyCollectionChangedAction.Reset && _active != null)
                foreach (var f in _active.Filters) { f.PropertyChanged -= Filter_PropertyChanged; f.PropertyChanged += Filter_PropertyChanged; }
            RedrawOnly();
            ScheduleApply(true);
        }

        private void Filter_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(EqFilter.Freq) or nameof(EqFilter.Gain) or nameof(EqFilter.Q) or nameof(EqFilter.Kind) or nameof(EqFilter.Enabled))
                ScheduleApply(true);
        }

        private void ScheduleApply(bool save)
        {
            _applyTimer.Stop(); _applyTimer.Start();
            if (save) { _saveTimer.Stop(); _saveTimer.Start(); }
        }

        private void SaveAll()
        {
            try
            {
                _storage.SaveSettings(_s);
                if (_active != null) _storage.SavePreset(_active);
            }
            catch (Exception ex) { SetStatus("Kaydedilemedi: " + ex.Message); }

            if (_needsListRefresh && _view != null)
            {
                _needsListRefresh = false;
                _suppressSelection = true;
                _view.Refresh();
                PresetList.SelectedItem = _active;
                _suppressSelection = false;
            }
        }

        // ================== Sesi uygula ==================

        private Preset? BPreset => _s.CompareBPresetId == FlatId ? null : _presets.FirstOrDefault(p => p.Id == _s.CompareBPresetId);
        private bool BIsFlat => _s.CompareBPresetId == null || _s.CompareBPresetId == FlatId || BPreset == null;
        private bool PlayingB => AbToggle.IsChecked == true;

        private void ApplyNow()
        {
            if (!_initialized) return;
            double vol = _audio.VolumeDb;
            _lastLoudness = ConfigBuilder.LoudnessGains(_s, vol);

            string text;
            double preamp;

            if (_s.Bypass)
            {
                text = ConfigBuilder.Render(_s, _active, vol);
                preamp = 0;
            }
            else
            {
                var fa = ConfigBuilder.Collect(_s, _active, vol).Select(l => l.Filter).ToList();
                double preA = ConfigBuilder.SafePreamp(fa, _s.SafetyMarginDb);

                if (!_abArmed)
                {
                    preamp = preA;
                    text = ConfigBuilder.Render(_s, _active, vol, preamp);
                }
                else
                {
                    // Seviye eşlemeli A/B: iki taraf da 100 Hz – 8 kHz ortalamasında aynı yükseklikte çalar.
                    var fb = BIsFlat ? new List<EqFilter>() : ConfigBuilder.Collect(_s, BPreset, vol).Select(l => l.Filter).ToList();
                    double preB = ConfigBuilder.SafePreamp(fb, _s.SafetyMarginDb);
                    double avgA = ConfigBuilder.AverageLevelDb(fa), avgB = ConfigBuilder.AverageLevelDb(fb);
                    double target = Math.Min(avgA + preA, avgB + preB);
                    preA = Math.Round(target - avgA, 1);
                    preB = Math.Round(target - avgB, 1);

                    if (PlayingB)
                    {
                        preamp = preB;
                        text = BIsFlat
                            ? "# Mest EQ A/B: B tarafı (EQ'suz)\r\nChannel: all\r\nPreamp: " + preB.ToString("0.0", CultureInfo.InvariantCulture) + " dB\r\n"
                            : ConfigBuilder.Render(_s, BPreset, vol, preB, "A/B: B tarafı " + BPreset!.Name);
                    }
                    else
                    {
                        preamp = preA;
                        text = ConfigBuilder.Render(_s, _active, vol, preA, "A/B: A tarafı " + (_active?.Name ?? ""));
                    }
                }
            }

            if (_apo.IsInstalled)
            {
                if (text != _lastWritten)
                {
                    if (_apo.Write(text)) _lastWritten = text;
                    else SetStatus(_apo.LastError ?? "Ayar yazılamadı.");
                }
            }

            PreampText.Text = preamp.ToString("0.0", CultureInfo.InvariantCulture) + " dB";
            RedrawOnly(preamp);
            UpdateLoudnessInfo();
        }

        /// <summary>A/B modu açık mı (A ya da B çalıyor olabilir).</summary>
        private bool _abArmed;

        private double _lastPreamp;

        private void RedrawOnly(double? preamp = null)
        {
            if (preamp.HasValue) _lastPreamp = preamp.Value;
            double vol = _audio.VolumeDb;
            Graph.ShowBase = ShowBaseChk.IsChecked == true;
            Graph.ShowTaste = ShowTasteChk.IsChecked == true;
            Graph.ShowPreset = ShowPresetChk.IsChecked == true;

            if (_s.Bypass || (_abArmed && PlayingB && BIsFlat))
            {
                Graph.Update(Array.Empty<EqFilter>(), Array.Empty<EqFilter>(), Array.Empty<EqFilter>(), Array.Empty<EqFilter>(), _lastPreamp, Array.Empty<EqFilter>());
                return;
            }

            var shown = _abArmed && PlayingB ? BPreset : _active;
            var baseF = _s.BaseEnabled ? _s.BaseFilters.Where(f => f.Enabled) : Enumerable.Empty<EqFilter>();
            var presetF = shown?.Filters.Where(f => f.Enabled) ?? Enumerable.Empty<EqFilter>();
            IList<EqFilter> handles = shown?.Filters ?? (IList<EqFilter>)Array.Empty<EqFilter>();
            Graph.Update(baseF, ConfigBuilder.TasteFilters(_s), presetF, ConfigBuilder.LoudnessFilters(_s, vol), _lastPreamp, handles);
        }

        private void OnVolume()
        {
            _audio.ReadVolume();
            var now = ConfigBuilder.LoudnessGains(_s, _audio.VolumeDb);
            if (now != _lastLoudness) ApplyNow();
            else UpdateLoudnessInfo();
        }

        private void UpdateLoudnessInfo()
        {
            var (b, t) = ConfigBuilder.LoudnessGains(_s, _audio.VolumeDb);
            string vol = double.IsNaN(_audio.VolumeDb) ? "?" : _audio.VolumeDb.ToString("0.0", CultureInfo.InvariantCulture);
            string refDb = _s.LoudnessReferenceDb.ToString("0.0", CultureInfo.InvariantCulture);
            LoudInfo.Text = !_s.LoudnessEnabled
                ? $"Kapalı. Windows sesi şu an {vol} dB."
                : (b <= 0 && t <= 0
                    ? $"Referans seviyede ({refDb} dB), telafi yok."
                    : $"Ses {vol} dB: bas +{b:0.0}, tiz +{t:0.0} dB telafi.");
        }

        // ================== Durum ==================

        private void UpdateStatus()
        {
            var good = (Brush)FindResource("Sage");
            var bad = (Brush)FindResource("Rose");
            var warn = (Brush)FindResource("Brass");
            var muted = (Brush)FindResource("Muted");

            if (!_apo.IsInstalled)
            {
                ApoDot.Fill = bad;
                ApoText.Text = "Ses motoru bulunamadı; Mest kurulumunu yeniden çalıştır";
                FixDeviceBtn.Visibility = Visibility.Collapsed;
            }
            else
            {
                switch (_apo.CheckDevice(_audio.DeviceId))
                {
                    case ApoDeviceState.Active:
                        ApoDot.Fill = good; ApoText.Text = "Ses motoru bu cihazda çalışıyor";
                        FixDeviceBtn.Visibility = Visibility.Collapsed;
                        break;
                    case ApoDeviceState.NotActive:
                        ApoDot.Fill = bad; ApoText.Text = "Ses motoru bu cihazda etkin değil";
                        FixDeviceBtn.Visibility = Visibility.Visible;
                        break;
                    default:
                        ApoDot.Fill = muted; ApoText.Text = "Ses motoru durumu okunamadı";
                        FixDeviceBtn.Visibility = Visibility.Visible;
                        break;
                }
            }

            string rate = _audio.SampleRate > 0
                ? (_audio.SampleRate / 1000.0).ToString("0.#", CultureInfo.InvariantCulture) + " kHz" + (_audio.BitDepth > 0 ? $", {_audio.BitDepth} bit" : "")
                : "";
            if (string.IsNullOrEmpty(_audio.DeviceId))
            {
                DevDot.Fill = bad; DevText.Text = "Çıkış cihazı bulunamadı";
            }
            else if (_audio.IsDawnPro)
            {
                DevDot.Fill = good; DevText.Text = "Dawn Pro bağlı" + (rate.Length > 0 ? ", " + rate : "");
            }
            else
            {
                DevDot.Fill = warn; DevText.Text = "Varsayılan çıkış: " + _audio.DeviceName;
            }
            DevText.ToolTip = _audio.DeviceName + (rate.Length > 0 ? " (" + rate + ")" : "");
        }

        private void SetStatus(string text) => StatusText.Text = text;

        // ================== Çalan şarkı ==================

        private void OnNowPlaying(NowPlaying np)
        {
            _now = np;
            UpdateNowPlayingUi();
            if (_s.AutoSwitch) AutoMatch(notify: !IsVisible);
        }

        private void UpdateNowPlayingUi()
        {
            if (_now.IsEmpty)
            {
                NowTitle.Text = "Şu an bir şey çalmıyor";
                NowSub.Text = "Spotify'da bir şarkı başlattığında burada görünür.";
            }
            else
            {
                NowTitle.Text = string.IsNullOrWhiteSpace(_now.Title) ? "(isimsiz parça)" : _now.Title;
                NowSub.Text = _now.Artist + (string.IsNullOrWhiteSpace(_now.Album) ? "" : ", " + _now.Album + " albümünden");
                NowSub.ToolTip = string.IsNullOrEmpty(_now.Source) ? null : "Kaynak: " + _now.Source;
            }
            BindTrackBtn.IsEnabled = !_now.IsEmpty;
            BindArtistBtn.IsEnabled = !string.IsNullOrWhiteSpace(_now.Artist);
            BindAlbumBtn.IsEnabled = !string.IsNullOrWhiteSpace(_now.Album) && !string.IsNullOrWhiteSpace(_now.Artist);
        }

        private void AutoMatch(bool notify)
        {
            var (preset, level) = PresetMatcher.Match(_presets, _now, _s.DefaultPresetId);
            if (preset == null) return;
            string why = level switch
            {
                PresetMatcher.MatchLevel.Track => "bu şarkıya bağlı",
                PresetMatcher.MatchLevel.Album => "bu albüme bağlı",
                PresetMatcher.MatchLevel.Artist => "bu sanatçıya bağlı",
                _ => "varsayılan preset"
            };
            if (!ReferenceEquals(preset, _active))
            {
                SetActive(preset, why, select: true);
                if (notify) (Application.Current as App)?.Notify("Mest: " + preset.Name, _now.Artist + ", " + _now.Title);
            }
            else AppliedWhy.Text = why;
        }

        // ================== Preset listesi ==================

        private void PresetList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressSelection || _loading) return;
            if (PresetList.SelectedItem is Preset p && !ReferenceEquals(p, _active))
                SetActive(p, "elle seçildi; sonraki şarkıya kadar", select: false);
        }

        private void AddPresetToList(Preset p)
        {
            _presets.Add(p);
            _storage.SavePreset(p);
            _suppressSelection = true;
            _view?.Refresh();
            _suppressSelection = false;
            SetActive(p, "yeni preset", select: true);
            PresetName.Focus();
            PresetName.SelectAll();
        }

        private void NewPreset_Click(object sender, RoutedEventArgs e) => AddPresetToList(new Preset { Name = "Yeni preset" });

        private void DuplicatePreset_Click(object sender, RoutedEventArgs e)
        {
            if (_active != null) AddPresetToList(_active.Clone(_active.Name + " (kopya)"));
        }

        private void DeletePreset_Click(object sender, RoutedEventArgs e)
        {
            if (_active == null || _active.BuiltIn) return;
            var r = MessageBox.Show(this, $"\"{_active.Name}\" silinsin mi? Bağlı şarkı ve sanatçılar varsayılan presete döner.",
                                    "Preseti sil", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (r != MessageBoxResult.Yes) return;
            var gone = _active;
            _storage.DeletePreset(gone);
            _presets.Remove(gone);
            if (_s.DefaultPresetId == gone.Id) _s.DefaultPresetId = DefaultPresets.NeutralId;
            if (_s.CompareBPresetId == gone.Id) _s.CompareBPresetId = FlatId;
            _suppressSelection = true;
            _view?.Refresh();
            _suppressSelection = false;
            var next = _presets.FirstOrDefault(p => p.Id == _s.DefaultPresetId) ?? _presets.First();
            SetActive(next, "varsayılan preset", select: true);
            UpdateAbInfo();
        }

        private void PresetMeta_Changed(object sender, TextChangedEventArgs e)
        {
            if (_loading || _active == null) return;
            if (!string.IsNullOrWhiteSpace(PresetName.Text) && PresetName.Text != _active.Name)
            {
                _active.Name = PresetName.Text.Trim();
                AppliedName.Text = _active.Name;
                _needsListRefresh = true;
            }
            _active.Notes = PresetNotes.Text;
            _saveTimer.Stop(); _saveTimer.Start();
        }

        private void MakeDefault_Click(object sender, RoutedEventArgs e)
        {
            if (_active == null) return;
            _s.DefaultPresetId = _active.Id;
            UpdateDefaultButton();
            SetStatus($"Eşleşmeyen şarkılarda \"{_active.Name}\" çalacak.");
            _saveTimer.Stop(); _saveTimer.Start();
        }

        private void UpdateDefaultButton()
        {
            bool isDefault = _active != null && _active.Id == _s.DefaultPresetId;
            DefaultBtn.IsEnabled = !isDefault;
            DefaultBtn.Content = isDefault ? "Varsayılan preset bu" : "Eşleşme yoksa bunu kullan";
        }

        // ================== Filtreler ==================

        private void AddFilter(EqFilter f)
        {
            if (_active == null) return;
            _active.Filters.Add(f);
        }

        private void AddFilter_Click(object sender, RoutedEventArgs e) => AddFilter(new EqFilter(FilterKind.Peak, 1000, 0, 1.0));

        private void RemoveFilter_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is EqFilter f) _active?.Filters.Remove(f);
        }

        private void CycleKind_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is EqFilter f) f.CycleKind();
        }

        private void NumBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter || sender is not TextBox tb) return;
            tb.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
            tb.SelectAll();
            e.Handled = true;
        }

        private void ImportPreset_Click(object sender, RoutedEventArgs e)
        {
            if (_active == null) return;
            string text = SafeClipboard();
            var filters = ApoImporter.Parse(text, out int skipped);
            if (filters.Count == 0)
            {
                MessageBox.Show(this,
                    "Panoda filtre satırı bulunamadı.\n\nŞu biçimdeki satırları kopyalayıp tekrar dene:\nFilter 1: ON PK Fc 120 Hz Gain -2.5 dB Q 1.20",
                    "APO metni yapıştır", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            if (_active.Filters.Count > 0)
            {
                var r = MessageBox.Show(this, $"{filters.Count} filtre bulundu. \"{_active.Name}\" içindeki mevcut filtrelerin yerine geçsin mi?",
                                        "APO metni yapıştır", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (r != MessageBoxResult.Yes) return;
            }
            _active.Filters.Clear();
            foreach (var f in filters) _active.Filters.Add(f);
            SetStatus($"{filters.Count} filtre içe aktarıldı" + (skipped > 0 ? $", desteklenmeyen {skipped} satır atlandı." : "."));
        }

        private void ImportBase_Click(object sender, RoutedEventArgs e)
        {
            string text = SafeClipboard();
            var filters = ApoImporter.Parse(text, out int skipped);
            if (filters.Count == 0)
            {
                var r = MessageBox.Show(this,
                    "Panoda AutoEQ filtresi bulunamadı.\n\n" +
                    "Altruva'nın ölçümünden düzeltme eğrisi almak için:\n" +
                    "1. Açılacak squig.link sayfasında Equalizer sekmesine geç.\n" +
                    "2. Hedef olarak Harman ya da beğendiğin bir hedef seç, AutoEQ'ya bas.\n" +
                    "3. Export Parametric EQ ile çıkan metni kopyala.\n" +
                    "4. Mest'e dönüp bu butona tekrar bas.\n\nSayfa şimdi açılsın mı?",
                    "Kulaklık düzeltmesi", MessageBoxButton.YesNo, MessageBoxImage.Information);
                if (r == MessageBoxResult.Yes)
                {
                    try { Process.Start(new ProcessStartInfo("https://earphonesarchive.squig.link/headphones/?share=Kiwi_Ears_Altruva_(center)") { UseShellExecute = true }); }
                    catch { SetStatus("Tarayıcı açılamadı."); }
                }
                return;
            }
            _s.BaseFilters.Clear();
            foreach (var f in filters) _s.BaseFilters.Add(f);
            _s.BaseSource = "Panodan içe aktarıldı, " + DateTime.Now.ToString("d MMMM yyyy", new CultureInfo("tr-TR"));
            _s.BaseEnabled = true;
            BaseChk.IsChecked = true;
            UpdateLayerLabels();
            ScheduleApply(true);
            SetStatus($"Kulaklık düzeltmesi güncellendi: {filters.Count} filtre" + (skipped > 0 ? $", {skipped} satır atlandı." : "."));
        }

        private void ResetBase_Click(object sender, RoutedEventArgs e)
        {
            _s.BaseFilters.Clear();
            foreach (var f in DefaultPresets.AltruvaEstimatedBase()) _s.BaseFilters.Add(f);
            _s.BaseSource = new AppSettings().BaseSource;
            UpdateLayerLabels();
            ScheduleApply(true);
        }

        private static string SafeClipboard()
        {
            try { return Clipboard.ContainsText() ? Clipboard.GetText() : ""; }
            catch { return ""; }
        }

        // ================== Bağlantılar (otomatik geçiş) ==================

        private void RefreshBindings()
        {
            if (_active == null) { BindingList.ItemsSource = null; return; }
            var items = _active.Tracks.Select(k => new BindingItem("track", k))
                .Concat(_active.Albums.Select(k => new BindingItem("album", k)))
                .Concat(_active.Artists.Select(k => new BindingItem("artist", k)))
                .ToList();
            BindingList.ItemsSource = items;
        }

        private void Bind(string kind)
        {
            if (_active == null || _now.IsEmpty) return;
            string key = kind switch
            {
                "track" => PresetMatcher.TrackKey(_now),
                "album" => PresetMatcher.AlbumKey(_now),
                _ => PresetMatcher.ArtistKey(_now)
            };
            // Bir şarkı/albüm/sanatçı aynı anda tek presete bağlı olur.
            foreach (var p in _presets)
            {
                var list = ListFor(p, kind);
                if (!ReferenceEquals(p, _active) && list.Remove(key)) _storage.SavePreset(p);
            }
            var mine = ListFor(_active, kind);
            if (!mine.Contains(key)) mine.Add(key);
            _storage.SavePreset(_active);
            RefreshBindings();
            _needsListRefresh = true;
            _saveTimer.Stop(); _saveTimer.Start();
            AutoMatch(notify: false);
            SetStatus(kind switch
            {
                "track" => $"\"{_now.Title}\" artık hep \"{_active.Name}\" ile çalacak.",
                "album" => $"{_now.Album} albümü artık \"{_active.Name}\" ile çalacak.",
                _ => $"{_now.Artist} şarkıları artık \"{_active.Name}\" ile çalacak."
            });
        }

        private static List<string> ListFor(Preset p, string kind) => kind switch
        {
            "track" => p.Tracks,
            "album" => p.Albums,
            _ => p.Artists
        };

        private void BindTrack_Click(object sender, RoutedEventArgs e) => Bind("track");
        private void BindAlbum_Click(object sender, RoutedEventArgs e) => Bind("album");
        private void BindArtist_Click(object sender, RoutedEventArgs e) => Bind("artist");

        private void Unbind_Click(object sender, RoutedEventArgs e)
        {
            if (_active == null || (sender as FrameworkElement)?.Tag is not BindingItem item) return;
            ListFor(_active, item.Kind).Remove(item.Key);
            _storage.SavePreset(_active);
            RefreshBindings();
            _needsListRefresh = true;
            _saveTimer.Stop(); _saveTimer.Start();
        }

        // ================== Katmanlar ==================

        private void Layer_Changed(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            _s.BaseEnabled = BaseChk.IsChecked == true;
            _s.TasteEnabled = TasteChk.IsChecked == true;
            _s.LoudnessEnabled = LoudChk.IsChecked == true;
            _s.CrossfeedEnabled = XfeedChk.IsChecked == true;
            ScheduleApply(true);
        }

        private void Taste_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_loading || !_initialized) return;
            _s.TasteBass = BassSl.Value;
            _s.TasteWarmth = WarmSl.Value;
            _s.TastePresence = PresSl.Value;
            _s.TasteAir = AirSl.Value;
            UpdateLayerLabels();
            RedrawOnly();
            ScheduleApply(true);
        }

        private void Space_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_loading || !_initialized) return;
            _s.LoudnessAmount = LoudSl.Value;
            _s.CrossfeedAmount = XfeedSl.Value;
            _s.Balance = BalSl.Value;
            UpdateLayerLabels();
            ScheduleApply(true);
        }

        private void BalReset_DoubleClick(object sender, MouseButtonEventArgs e) => BalSl.Value = 0;

        private void LoudnessRef_Click(object sender, RoutedEventArgs e)
        {
            _audio.ReadVolume();
            if (double.IsNaN(_audio.VolumeDb)) { SetStatus("Ses seviyesi okunamadı."); return; }
            _s.LoudnessReferenceDb = _audio.VolumeDb;
            _s.LoudnessEnabled = true;
            LoudChk.IsChecked = true;
            SetStatus("Bu ses seviyesi referans alındı. Daha kısık dinlerken bas ve tiz telafi edilecek.");
            ScheduleApply(true);
        }

        private void GraphLegend_Click(object sender, RoutedEventArgs e) => RedrawOnly();

        // ================== Alt bant ==================

        public void SetBypass(bool bypass)
        {
            _s.Bypass = bypass;
            EqOnChk.IsChecked = !bypass;
            ApplyNow();
            _saveTimer.Stop(); _saveTimer.Start();
            BypassChanged?.Invoke(bypass);
        }

        private void EqOn_Click(object sender, RoutedEventArgs e) => SetBypass(EqOnChk.IsChecked != true);

        private void Auto_Click(object sender, RoutedEventArgs e)
        {
            _s.AutoSwitch = AutoChk.IsChecked == true;
            if (_s.AutoSwitch) AutoMatch(notify: false);
            _saveTimer.Stop(); _saveTimer.Start();
        }

        private void Ab_Click(object sender, RoutedEventArgs e)
        {
            _abArmed = true;
            ApplyNow();
            UpdateAbInfo();
        }

        private void AbFlat_Click(object sender, RoutedEventArgs e)
        {
            _s.CompareBPresetId = FlatId;
            _abArmed = true;
            UpdateAbInfo();
            ApplyNow();
            _saveTimer.Stop(); _saveTimer.Start();
        }

        private void AbSelected_Click(object sender, RoutedEventArgs e)
        {
            if (_active == null) return;
            _s.CompareBPresetId = _active.Id;
            _abArmed = true;
            UpdateAbInfo();
            ApplyNow();
            SetStatus($"B tarafı: \"{_active.Name}\". Şimdi listeden başka bir preset seç, A/B ile karşılaştır.");
            _saveTimer.Stop(); _saveTimer.Start();
        }

        private void UpdateAbInfo()
        {
            string b = BIsFlat ? "EQ'suz" : BPreset!.Name;
            AbInfo.Text = PlayingB ? $"B çalıyor: {b}" : $"A: {_active?.Name}  B: {b}";
        }

        // ================== Kapatma ==================

        private void FixDevice_Click(object sender, RoutedEventArgs e)
        {
            if (_apo.OpenDeviceSelector())
                SetStatus("Açılan pencerede Dawn Pro'yu işaretleyip onayla. İstenirse bilgisayarı yeniden başlat.");
            else
                SetStatus("Cihaz seçici açılamadı. Başlat menüsünden Equalizer APO Device Selector'ı çalıştır.");
        }

        private void OnClosing(object? sender, CancelEventArgs e)
        {
            var app = Application.Current as App;
            if (_s.CloseToTray && app != null && !app.ExitRequested)
            {
                e.Cancel = true;
                Hide();
                SaveAll();
                if (!_toldAboutTray)
                {
                    _toldAboutTray = true;
                    app.Notify("Mest arka planda", "Otomatik preset geçişi için çalışmaya devam ediyor. Kapatmak için tepsi simgesine sağ tıkla.");
                }
                return;
            }
            Shutdown();
        }

        public void Shutdown()
        {
            _applyTimer.Stop();
            _deviceTimer.Stop();
            _volumeTimer.Stop();
            _saveTimer.Stop();
            SaveAll();
            try { _audio.Dispose(); } catch { }
        }
    }
}
