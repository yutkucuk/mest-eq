using System;
using System.Linq;
using System.Threading;
using System.Windows;
using WinForms = System.Windows.Forms;

namespace MestEQ
{
    public partial class App : Application
    {
        private Mutex? _mutex;
        private WinForms.NotifyIcon? _tray;
        private WinForms.ToolStripMenuItem? _trayEqItem;
        private MainWindow? _main;

        public bool ExitRequested { get; private set; }

        protected override void OnStartup(StartupEventArgs e)
        {
            _mutex = new Mutex(true, "MestEQ.SingleInstance.v1", out bool created);
            if (!created)
            {
                // Zaten çalışıyor: ikinci örneği sessizce kapat.
                Shutdown();
                return;
            }

            base.OnStartup(e);
            bool startHidden = e.Args.Any(a => a.Equals("--tray", StringComparison.OrdinalIgnoreCase));

            _main = new MainWindow();
            MainWindow = _main;
            CreateTray();
            _main.Initialize();
            _main.BypassChanged += bypass => { if (_trayEqItem != null) _trayEqItem.Checked = !bypass; };

            if (!startHidden) _main.Show();
        }

        private void CreateTray()
        {
            System.Drawing.Icon? icon = null;
            try { icon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath ?? ""); } catch { }

            var menu = new WinForms.ContextMenuStrip();
            menu.Items.Add("Mest'i aç", null, (_, _) => ShowMain());
            _trayEqItem = new WinForms.ToolStripMenuItem("EQ açık") { Checked = true, CheckOnClick = true };
            _trayEqItem.Click += (_, _) => _main?.SetBypass(!_trayEqItem.Checked);
            menu.Items.Add(_trayEqItem);
            menu.Items.Add(new WinForms.ToolStripSeparator());
            menu.Items.Add("Çıkış", null, (_, _) => ExitApp());

            _tray = new WinForms.NotifyIcon
            {
                Icon = icon ?? System.Drawing.SystemIcons.Application,
                Text = "Mest EQ",
                Visible = true,
                ContextMenuStrip = menu
            };
            _tray.DoubleClick += (_, _) => ShowMain();
        }

        public void ShowMain()
        {
            if (_main == null) return;
            _main.Show();
            if (_main.WindowState == WindowState.Minimized) _main.WindowState = WindowState.Normal;
            _main.Activate();
        }

        public void Notify(string title, string text)
        {
            try { _tray?.ShowBalloonTip(2500, title, text, WinForms.ToolTipIcon.None); } catch { }
        }

        public void ExitApp()
        {
            ExitRequested = true;
            _main?.Shutdown();
            if (_tray != null) { _tray.Visible = false; _tray.Dispose(); }
            Shutdown();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            if (_tray != null) { _tray.Visible = false; _tray.Dispose(); }
            _mutex?.Dispose();
            base.OnExit(e);
        }
    }
}
