using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using MestEQ.Core;

namespace MestEQ.Controls
{
    /// <summary>
    /// Ses zincirinin canlı frekans yanıtı. Preset filtrelerinin tutamaçları sürüklenebilir:
    /// yatay = frekans, dikey = kazanç, tekerlek = genişlik (Q), çift tık = yeni filtre, sağ tık = sil.
    /// </summary>
    public sealed class ResponseGraph : FrameworkElement
    {
        private static readonly double[] Grid = Biquad.LogGrid(420);
        private const double FMin = 20, FMax = 20000;

        private double[] _base = new double[Grid.Length];
        private double[] _taste = new double[Grid.Length];
        private double[] _preset = new double[Grid.Length];
        private double[] _loud = new double[Grid.Length];
        private double[] _total = new double[Grid.Length];
        private double _preamp;
        private IList<EqFilter> _handles = Array.Empty<EqFilter>();
        private int _drag = -1;
        private int _hover = -1;

        public bool ShowBase { get; set; } = true;
        public bool ShowTaste { get; set; } = true;
        public bool ShowPreset { get; set; } = true;

        public event Action? FiltersEdited;
        public event Action<double, double>? AddFilterRequested;
        public event Action<EqFilter>? RemoveFilterRequested;
        public event Action<EqFilter?>? HandleSelected;

        // Renkler App.xaml'daki paletle aynı.
        private static readonly Brush Bg = Frozen(Color.FromRgb(0x24, 0x1A, 0x1E));
        private static readonly Pen GridMinor = FrozenPen(Color.FromArgb(0x22, 0xEF, 0xE6, 0xD6), 1);
        private static readonly Pen GridMajor = FrozenPen(Color.FromArgb(0x3A, 0xEF, 0xE6, 0xD6), 1);
        private static readonly Pen ZeroLine = FrozenPen(Color.FromArgb(0x70, 0xEF, 0xE6, 0xD6), 1);
        private static readonly Pen BasePen = FrozenPen(Color.FromRgb(0x7F, 0xA3, 0xB8), 1.5);
        private static readonly Pen TastePen = FrozenPen(Color.FromRgb(0x9D, 0xB0, 0x8A), 1.5);
        private static readonly Pen PresetPen = FrozenPen(Color.FromRgb(0xD4, 0xA8, 0x57), 1.5);
        private static readonly Pen LoudPen = FrozenPen(Color.FromRgb(0xC7, 0x8B, 0xA6), 1.2, true);
        private static readonly Pen TotalPen = FrozenPen(Color.FromRgb(0xEF, 0xE6, 0xD6), 2.6);
        private static readonly Brush TotalFill = Frozen(Color.FromArgb(0x26, 0xD4, 0xA8, 0x57));
        private static readonly Brush Label = Frozen(Color.FromRgb(0xA8, 0x96, 0x8C));
        private static readonly Brush HandleFill = Frozen(Color.FromRgb(0xD4, 0xA8, 0x57));
        private static readonly Brush HandleText = Frozen(Color.FromRgb(0x24, 0x1A, 0x1E));
        private static readonly Pen HandleRing = FrozenPen(Color.FromRgb(0xEF, 0xE6, 0xD6), 2);
        private static readonly Pen PreampPen = FrozenPen(Color.FromArgb(0x90, 0xC7, 0x6B, 0x5A), 1, true);
        private static readonly Typeface Face = new(new FontFamily("Bahnschrift, Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        private static readonly Typeface FaceBold = new(new FontFamily("Bahnschrift, Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);

        private static Brush Frozen(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }
        private static Pen FrozenPen(Color c, double w, bool dashed = false)
        {
            var p = new Pen(Frozen(c), w) { LineJoin = PenLineJoin.Round, StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            if (dashed) p.DashStyle = new DashStyle(new double[] { 3, 3 }, 0);
            p.Freeze();
            return p;
        }

        public ResponseGraph()
        {
            ClipToBounds = true;
            Focusable = true;
            FocusVisualStyle = null;
        }

        public void Update(IEnumerable<EqFilter> baseF, IEnumerable<EqFilter> tasteF, IEnumerable<EqFilter> presetF,
                           IEnumerable<EqFilter> loudF, double preamp, IList<EqFilter> handles)
        {
            _base = Biquad.Response(baseF, Grid);
            _taste = Biquad.Response(tasteF, Grid);
            _preset = Biquad.Response(presetF, Grid);
            _loud = Biquad.Response(loudF, Grid);
            _total = new double[Grid.Length];
            for (int i = 0; i < Grid.Length; i++) _total[i] = _base[i] + _taste[i] + _preset[i] + _loud[i];
            _preamp = preamp;
            _handles = handles;
            InvalidateVisual();
        }

        // ---- Koordinatlar ----
        private Rect Plot => new(46, 14, Math.Max(10, ActualWidth - 46 - 14), Math.Max(10, ActualHeight - 14 - 28));

        private double Range
        {
            get
            {
                double m = 12;
                foreach (var v in _total) m = Math.Max(m, Math.Abs(v) + 1);
                foreach (var h in _handles) m = Math.Max(m, Math.Abs(h.Gain) + 1);
                return Math.Min(24, Math.Ceiling(m / 3) * 3);
            }
        }

        private double X(double f) { var p = Plot; return p.Left + p.Width * (Math.Log10(f) - Math.Log10(FMin)) / (Math.Log10(FMax) - Math.Log10(FMin)); }
        private double Y(double db) { var p = Plot; double r = Range; return p.Top + p.Height * (r - db) / (2 * r); }
        private double FreqAt(double x) { var p = Plot; double t = Math.Clamp((x - p.Left) / p.Width, 0, 1); return Math.Pow(10, Math.Log10(FMin) + t * (Math.Log10(FMax) - Math.Log10(FMin))); }
        private double DbAt(double y) { var p = Plot; double r = Range; double t = Math.Clamp((y - p.Top) / p.Height, 0, 1); return r - t * 2 * r; }

        protected override void OnRender(DrawingContext dc)
        {
            double dip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            dc.DrawRoundedRectangle(Bg, null, new Rect(0, 0, ActualWidth, ActualHeight), 10, 10);
            var p = Plot;
            double r = Range;

            // Izgara
            double[] majors = { 20, 50, 100, 200, 500, 1000, 2000, 5000, 10000, 20000 };
            for (int dec = 1; dec <= 4; dec++)
            {
                for (int k = 1; k <= 9; k++)
                {
                    double f = k * Math.Pow(10, dec);
                    if (f < FMin || f > FMax) continue;
                    bool major = majors.Contains(f);
                    dc.DrawLine(major ? GridMajor : GridMinor, new Point(X(f), p.Top), new Point(X(f), p.Bottom));
                }
            }
            foreach (var f in majors)
            {
                string t = f >= 1000 ? (f / 1000).ToString("0", CultureInfo.InvariantCulture) + "k" : f.ToString("0", CultureInfo.InvariantCulture);
                var ft = Text(t, 11, Label, dip);
                double x = Math.Clamp(X(f) - ft.Width / 2, p.Left - 4, p.Right - ft.Width);
                dc.DrawText(ft, new Point(x, p.Bottom + 7));
            }
            for (double db = -r; db <= r + 0.01; db += 3)
            {
                bool zero = Math.Abs(db) < 0.01;
                bool labeled = Math.Abs(db % 6) < 0.01;
                dc.DrawLine(zero ? ZeroLine : (labeled ? GridMajor : GridMinor), new Point(p.Left, Y(db)), new Point(p.Right, Y(db)));
                if (labeled)
                {
                    var ft = Text((db > 0 ? "+" : "") + db.ToString("0", CultureInfo.InvariantCulture), 11, Label, dip);
                    dc.DrawText(ft, new Point(p.Left - ft.Width - 8, Y(db) - ft.Height / 2));
                }
            }

            dc.PushClip(new RectangleGeometry(p));

            // Toplam eğrinin altı (sıfır çizgisine kadar)
            var fill = new StreamGeometry();
            using (var g = fill.Open())
            {
                g.BeginFigure(new Point(X(Grid[0]), Y(0)), true, true);
                for (int i = 0; i < Grid.Length; i++) g.LineTo(new Point(X(Grid[i]), Y(_total[i])), true, false);
                g.LineTo(new Point(X(Grid[^1]), Y(0)), true, false);
            }
            fill.Freeze();
            dc.DrawGeometry(TotalFill, null, fill);

            if (ShowBase) Curve(dc, _base, BasePen);
            if (ShowTaste) Curve(dc, _taste, TastePen);
            if (ShowPreset) Curve(dc, _preset, PresetPen);
            if (_loud.Any(v => Math.Abs(v) > 0.01)) Curve(dc, _loud, LoudPen);
            Curve(dc, _total, TotalPen);

            // Preamp çizgisi: toplam eğrinin tepesi ne kadar indirildi
            if (_preamp < -0.05)
            {
                double y = Y(-_preamp);
                dc.DrawLine(PreampPen, new Point(p.Left, y), new Point(p.Right, y));
                var ft = Text("tavan (preamp " + _preamp.ToString("0.0", CultureInfo.InvariantCulture) + " dB)", 10.5, Label, dip);
                dc.DrawText(ft, new Point(p.Right - ft.Width - 6, y - ft.Height - 2));
            }

            // Tutamaçlar
            for (int i = 0; i < _handles.Count; i++)
            {
                var h = _handles[i];
                var c = new Point(X(h.Freq), Y(h.Gain));
                double rad = (i == _drag || i == _hover) ? 11 : 9;
                dc.DrawEllipse(h.Enabled ? HandleFill : Label, i == _drag || i == _hover ? HandleRing : null, c, rad, rad);
                var num = Text((i + 1).ToString(CultureInfo.InvariantCulture), 11, HandleText, dip, true);
                dc.DrawText(num, new Point(c.X - num.Width / 2, c.Y - num.Height / 2));
            }

            if (_drag >= 0 && _drag < _handles.Count)
            {
                var h = _handles[_drag];
                var info = Text($"{h.FreqLabel}   {h.GainLabel}   {h.QLabel}", 12, HandleFill, dip, true);
                double x = Math.Clamp(X(h.Freq) - info.Width / 2, p.Left + 4, p.Right - info.Width - 4);
                double y = Y(h.Gain) - 34;
                if (y < p.Top + 2) y = Y(h.Gain) + 18;
                dc.DrawText(info, new Point(x, y));
            }
            dc.Pop();
        }

        private void Curve(DrawingContext dc, double[] data, Pen pen)
        {
            var geo = new StreamGeometry();
            using (var g = geo.Open())
            {
                g.BeginFigure(new Point(X(Grid[0]), Y(data[0])), false, false);
                for (int i = 1; i < Grid.Length; i++) g.LineTo(new Point(X(Grid[i]), Y(data[i])), true, true);
            }
            geo.Freeze();
            dc.DrawGeometry(null, pen, geo);
        }

        private static FormattedText Text(string s, double size, Brush b, double dip, bool bold = false) =>
            new(s, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, bold ? FaceBold : Face, size, b, dip);

        // ---- Fare etkileşimi ----
        private int HitTest(Point pt)
        {
            int best = -1; double bestD = 14;
            for (int i = 0; i < _handles.Count; i++)
            {
                var c = new Point(X(_handles[i].Freq), Y(_handles[i].Gain));
                double d = (c - pt).Length;
                if (d < bestD) { bestD = d; best = i; }
            }
            return best;
        }

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            Focus();
            var pt = e.GetPosition(this);
            if (e.ClickCount == 2 && HitTest(pt) < 0 && Plot.Contains(pt))
            {
                AddFilterRequested?.Invoke(FreqAt(pt.X), Math.Round(DbAt(pt.Y) * 2) / 2);
                e.Handled = true;
                return;
            }
            _drag = HitTest(pt);
            if (_drag >= 0)
            {
                CaptureMouse();
                HandleSelected?.Invoke(_handles[_drag]);
                InvalidateVisual();
                e.Handled = true;
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            var pt = e.GetPosition(this);
            if (_drag >= 0 && _drag < _handles.Count && e.LeftButton == MouseButtonState.Pressed)
            {
                var h = _handles[_drag];
                double f = FreqAt(pt.X);
                h.Freq = f < 1000 ? Math.Round(f) : Math.Round(f / 10) * 10;
                bool fine = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
                double g = DbAt(pt.Y);
                h.Gain = fine ? Math.Round(g * 10) / 10 : Math.Round(g * 2) / 2;
                FiltersEdited?.Invoke();
                return;
            }
            int hv = HitTest(pt);
            if (hv != _hover) { _hover = hv; Cursor = hv >= 0 ? Cursors.SizeAll : Cursors.Arrow; InvalidateVisual(); }
        }

        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            if (_drag >= 0) { _drag = -1; ReleaseMouseCapture(); InvalidateVisual(); FiltersEdited?.Invoke(); }
        }

        protected override void OnMouseRightButtonUp(MouseButtonEventArgs e)
        {
            int i = HitTest(e.GetPosition(this));
            if (i >= 0) { RemoveFilterRequested?.Invoke(_handles[i]); e.Handled = true; }
        }

        protected override void OnMouseWheel(MouseWheelEventArgs e)
        {
            int i = HitTest(e.GetPosition(this));
            if (i < 0) return;
            var h = _handles[i];
            h.Q = Math.Round(h.Q * (e.Delta > 0 ? 1.12 : 1 / 1.12), 2);
            FiltersEdited?.Invoke();
            e.Handled = true;
        }

        protected override void OnMouseLeave(MouseEventArgs e)
        {
            if (_hover >= 0) { _hover = -1; Cursor = Cursors.Arrow; InvalidateVisual(); }
        }
    }
}
