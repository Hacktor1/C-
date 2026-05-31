using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Animation;

using MessageBox = System.Windows.MessageBox;

namespace RedVision
{
    public partial class MainWindow : Window
    {
        [DllImport("magnification.dll")] public static extern bool MagInitialize();
        [DllImport("magnification.dll")] public static extern bool MagUninitialize();
        [DllImport("magnification.dll")] public static extern bool MagSetFullscreenColorEffect(ref MAGCOLORMATRIX pEffect);

        [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
        [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        private const int HOTKEY_ID = 9000;
        private const int WM_HOTKEY = 0x0312;
        private const uint MOD_ALT = 0x0001;
        private const uint MOD_CONTROL = 0x0002;
        private const uint MOD_SHIFT = 0x0004;

        [StructLayout(LayoutKind.Sequential)]
        public struct MAGCOLORMATRIX { [MarshalAs(UnmanagedType.ByValArray, SizeConst = 25)] public float[] transform; }

        private static readonly MAGCOLORMATRIX RedMatrix = new MAGCOLORMATRIX { transform = new float[] { 1f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 0f, 0f, 1f } };
        private static readonly MAGCOLORMATRIX IdentityMatrix = new MAGCOLORMATRIX { transform = new float[] { 1f, 0f, 0f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 0f, 0f, 1f } };

        private System.Windows.Forms.NotifyIcon _notifyIcon;
        private IntPtr _windowHandle;
        private HwndSource _hwndSource;
        private bool _isExplicitClose = false;
        private bool _isLoadingSettings = true;

        private string _language = "EN";

        private string _onlineVersion = "";
        private const string AktualniVerze = "1.2.0";
        private const string UrlVerze = "https://tvojedomena.cz/redvision/version.txt";
        private const string UrlProgramu = "https://tvojedomena.cz/redvision/RedVision.exe";
        private bool _isUpdateAvailable = false;

        // Mapování jazykových kódů na indexy ComboBoxu
        private static readonly Dictionary<string, int> LanguageIndexMap = new Dictionary<string, int>
        {
            { "EN", 0 }, { "CS", 1 }, { "DE", 2 }, { "FR", 3 }, { "ES", 4 },
            { "IT", 5 }, { "PT", 6 }, { "RU", 7 }, { "JA", 8 }, { "ZH", 9 },
            { "KO", 10 }, { "AR", 11 }, { "HI", 12 }
        };

        private static readonly Dictionary<int, string> IndexLanguageMap = new Dictionary<int, string>
        {
            { 0, "EN" }, { 1, "CS" }, { 2, "DE" }, { 3, "FR" }, { 4, "ES" },
            { 5, "IT" }, { 6, "PT" }, { 7, "RU" }, { 8, "JA" }, { 9, "ZH" },
            { 10, "KO" }, { 11, "AR" }, { 12, "HI" }
        };

        public MainWindow()
        {
            InitializeComponent();
            MagInitialize();
            LoadSettings();
            InitSystemTray();
        }

        private void Window_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
                this.DragMove();
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private string GetConfigPath()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string folder = Path.Combine(appData, "RedVision");
            if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);
            return Path.Combine(folder, "config.txt");
        }

        private void LoadSettings()
        {
            try
            {
                string path = GetConfigPath();
                if (File.Exists(path))
                {
                    foreach (var line in File.ReadAllLines(path))
                    {
                        var parts = line.Split('=');
                        if (parts.Length != 2) continue;

                        string key = parts[0];
                        string val = parts[1];

                        if (key == "Language") _language = val;
                        if (key == "Ctrl") ChkCtrl.IsChecked = bool.Parse(val);
                        if (key == "Alt") ChkAlt.IsChecked = bool.Parse(val);
                        if (key == "Shift") ChkShift.IsChecked = bool.Parse(val);
                        if (key == "Key") TxtKey.Text = val;
                    }
                }
            }
            catch { }

            if (LanguageIndexMap.TryGetValue(_language, out int index))
                CmbLang.SelectedIndex = index;
            else
                CmbLang.SelectedIndex = 0;

            ApplyLanguage();
            _isLoadingSettings = false;
        }

        private void SaveSettings()
        {
            if (_isLoadingSettings) return;
            try
            {
                var lines = new[]
                {
                    $"Language={_language}",
                    $"Ctrl={ChkCtrl.IsChecked}",
                    $"Alt={ChkAlt.IsChecked}",
                    $"Shift={ChkShift.IsChecked}",
                    $"Key={TxtKey.Text}"
                };
                File.WriteAllLines(GetConfigPath(), lines);
            }
            catch { }
        }

        private void InitSystemTray()
        {
            _notifyIcon = new System.Windows.Forms.NotifyIcon();
            _notifyIcon.Icon = System.Drawing.SystemIcons.Application;
            _notifyIcon.Visible = true;
            _notifyIcon.Text = "RedVision";
            _notifyIcon.DoubleClick += (s, e) => { ShowWindow(); };

            var contextMenu = new System.Windows.Forms.ContextMenuStrip();
            contextMenu.Items.Add("Open RedVision", null, (s, e) => ShowWindow());
            contextMenu.Items.Add("Exit", null, (s, e) => ExitApplication());
            _notifyIcon.ContextMenuStrip = contextMenu;
        }

        private void ShowWindow()
        {
            this.Show();
            this.WindowState = WindowState.Normal;
            this.Activate();
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            _windowHandle = new WindowInteropHelper(this).Handle;
            _hwndSource = HwndSource.FromHwnd(_windowHandle);
            _hwndSource.AddHook(HwndHook);
            RegisterCustomHotkey();
            _ = CheckForUpdatesAsync(isAutomatic: true);
        }

        // Animované přepínání panelů (animace vždy aktivní)
        private void SwitchPanels(UIElement hidePanel, UIElement showPanel)
        {
            DoubleAnimation fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(150));
            fadeOut.Completed += (s, e) =>
            {
                hidePanel.Visibility = Visibility.Collapsed;
                showPanel.Visibility = Visibility.Visible;

                DoubleAnimation fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150));
                showPanel.BeginAnimation(UIElement.OpacityProperty, fadeIn);
            };
            hidePanel.BeginAnimation(UIElement.OpacityProperty, fadeOut);
        }

        private void BtnSettings_Click(object sender, RoutedEventArgs e) => SwitchPanels(MainPanel, SettingsPanel);
        private void BtnBack_Click(object sender, RoutedEventArgs e) => SwitchPanels(SettingsPanel, MainPanel);

        private void CmbLang_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (IndexLanguageMap.TryGetValue(CmbLang.SelectedIndex, out string code))
                _language = code;
            else
                _language = "EN";

            ApplyLanguage();
            SaveSettings();
        }

        private void ApplyLanguage()
        {
            if (!IsInitialized) return;

            switch (_language)
            {
                case "CS":
                    TxtTrayInfo.Text = "Aplikace běží na pozadí v systémové liště.";
                    BtnBack.Content = "⬅ Zpět";
                    TxtSettingsTitle.Text = "Nastavení";
                    TxtDisplayCategory.Text = "Rozhraní a zobrazení";
                    TxtLangLabel.Text = "Jazyk:";
                    TxtHotkeyCategory.Text = "Klávesová zkratka";
                    TxtKeyLabel.Text = "Aktivační klávesa:";
                    SetUpdateBtnUI(_isUpdateAvailable ? "Instalovat" : "Aktuální", "⬇", _isUpdateAvailable ? (byte)48 : (byte)80, _isUpdateAvailable ? (byte)209 : (byte)80, _isUpdateAvailable ? (byte)88 : (byte)80);
                    break;

                default: // Všechny ostatní jazyky používají angličtinu (překlady lze rozšířit)
                    TxtTrayInfo.Text = "Application runs in the background tray.";
                    BtnBack.Content = "⬅ Back";
                    TxtSettingsTitle.Text = "Settings";
                    TxtDisplayCategory.Text = "Interface & Display";
                    TxtLangLabel.Text = "Language:";
                    TxtHotkeyCategory.Text = "Global Hotkey";
                    TxtKeyLabel.Text = "Activation Key:";
                    SetUpdateBtnUI(_isUpdateAvailable ? "Install" : "Up to date", "⬇", _isUpdateAvailable ? (byte)48 : (byte)80, _isUpdateAvailable ? (byte)209 : (byte)80, _isUpdateAvailable ? (byte)88 : (byte)80);
                    break;
            }
            UpdateStatusText(BtnToggleRed.IsChecked == true);
        }

        private void UpdateStatusText(bool isActive)
        {
            var txtMainBtn = BtnToggleRed.Template?.FindName("TxtMainBtn", BtnToggleRed) as System.Windows.Controls.TextBlock;

            if (isActive)
            {
                TxtStatus.Text = _language == "CS" ? "Červený režim AKTIVNÍ" : "Red Mode ACTIVE";
                TxtStatus.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 69, 58));

                if (txtMainBtn != null)
                    txtMainBtn.Text = _language == "CS" ? "DEAKTIVOVAT" : "DEACTIVATE";
            }
            else
            {
                TxtStatus.Text = _language == "CS" ? "Červený režim vypnut" : "Red Mode disabled";
                TxtStatus.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(170, 170, 170));

                if (txtMainBtn != null)
                    txtMainBtn.Text = _language == "CS" ? "AKTIVOVAT" : "ACTIVATE";
            }
        }

        private void RegisterCustomHotkey()
        {
            if (_windowHandle == IntPtr.Zero) return;
            UnregisterHotKey(_windowHandle, HOTKEY_ID);

            uint modifiers = 0;
            if (ChkCtrl.IsChecked == true) modifiers |= MOD_CONTROL;
            if (ChkAlt.IsChecked == true) modifiers |= MOD_ALT;
            if (ChkShift.IsChecked == true) modifiers |= MOD_SHIFT;

            if (TxtKey != null && !string.IsNullOrWhiteSpace(TxtKey.Text))
            {
                if (Enum.TryParse(TxtKey.Text.Trim(), true, out System.Windows.Input.Key parsedKey))
                {
                    uint vk = (uint)System.Windows.Input.KeyInterop.VirtualKeyFromKey(parsedKey);
                    RegisterHotKey(_windowHandle, HOTKEY_ID, modifiers, vk);
                }
            }
        }

        private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_HOTKEY && wParam.ToInt32() == HOTKEY_ID)
            {
                BtnToggleRed.IsChecked = !BtnToggleRed.IsChecked;
                ApplyColorFilter();
                handled = true;
            }
            return IntPtr.Zero;
        }

        private void HotkeyChanged(object sender, RoutedEventArgs e)
        {
            if (IsInitialized) RegisterCustomHotkey();
            SaveSettings();
        }

        private void TxtKey_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            if (IsInitialized) RegisterCustomHotkey();
            SaveSettings();
        }

        private void BtnToggleRed_Click(object sender, RoutedEventArgs e) => ApplyColorFilter();

        private void ApplyColorFilter()
        {
            bool isActive = BtnToggleRed.IsChecked == true;
            MAGCOLORMATRIX matrix = isActive ? RedMatrix : IdentityMatrix;
            MagSetFullscreenColorEffect(ref matrix);

            StateDot.Fill = new System.Windows.Media.SolidColorBrush(isActive ?
                System.Windows.Media.Color.FromRgb(48, 209, 88) :
                System.Windows.Media.Color.FromRgb(255, 69, 58));

            UpdateStatusText(isActive);
        }

        private void SetUpdateBtnUI(string text, string icon, byte r, byte g, byte b)
        {
            if (BtnUpdate == null) return;
            BtnUpdate.Content = $"{icon}  {text}";
            BtnUpdate.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(r, g, b));
        }

        private async Task CheckForUpdatesAsync(bool isAutomatic)
        {
            try
            {
                using (HttpClient client = new HttpClient())
                {
                    _onlineVersion = (await client.GetStringAsync(UrlVerze)).Trim();
                }

                if (Version.TryParse(_onlineVersion, out Version online) && Version.TryParse(AktualniVerze, out Version current))
                {
                    if (online > current)
                    {
                        _isUpdateAvailable = true;
                        SetUpdateBtnUI(_language == "CS" ? "Instalovat" : "Install", "⬇", 48, 209, 88);
                        return;
                    }
                }

                _isUpdateAvailable = false;
                SetUpdateBtnUI(_language == "CS" ? "Aktuální" : "Up to date", "⬇", 80, 80, 80);

                if (!isAutomatic)
                    MessageBox.Show(_language == "CS" ? "Máte nainstalovanou nejnovější verzi." : "You have the latest version.", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch
            {
                _isUpdateAvailable = false;
                SetUpdateBtnUI(_language == "CS" ? "Chyba" : "Error", "⚠", 80, 80, 80);
                if (!isAutomatic)
                    MessageBox.Show(_language == "CS" ? "Nepodařilo se spojit se serverem." : "Could not connect to server.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void BtnUpdate_Click(object sender, RoutedEventArgs e)
        {
            if (!_isUpdateAvailable)
            {
                BtnUpdate.IsEnabled = false;
                await CheckForUpdatesAsync(isAutomatic: false);
                BtnUpdate.IsEnabled = true;
                return;
            }

            BtnUpdate.IsEnabled = false;
            SetUpdateBtnUI(_language == "CS" ? "Stahování..." : "Downloading...", "⏳", 255, 159, 10);

            try
            {
                string cestaKApp = Environment.ProcessPath ?? "";
                if (string.IsNullOrEmpty(cestaKApp)) return;

                string slozkaApp = Path.GetDirectoryName(cestaKApp) ?? "";
                string novySouborTmp = Path.Combine(slozkaApp, "RedVision.new");

                using (HttpClient client = new HttpClient())
                {
                    byte[] data = await client.GetByteArrayAsync(UrlProgramu);
                    File.WriteAllBytes(novySouborTmp, data);
                }

                string batCesta = Path.Combine(slozkaApp, "updater.bat");
                string batObsah = $@"
@echo off
timeout /t 1 /nobreak > NUL
del ""{cestaKApp}""
move /y ""{novySouborTmp}"" ""{cestaKApp}""
start """" ""{cestaKApp}""
del ""%~f0""
";
                File.WriteAllText(batCesta, batObsah);
                Process.Start(new ProcessStartInfo { FileName = batCesta, CreateNoWindow = true, UseShellExecute = false });
                ExitApplication();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Chyba / Error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                BtnUpdate.IsEnabled = true;
                await CheckForUpdatesAsync(isAutomatic: true);
            }
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (!_isExplicitClose)
            {
                e.Cancel = true;
                this.Hide();
            }
            base.OnClosing(e);
        }

        private void ExitApplication()
        {
            _isExplicitClose = true;
            MAGCOLORMATRIX matrix = IdentityMatrix;
            MagSetFullscreenColorEffect(ref matrix);
            MagUninitialize();

            if (_windowHandle != IntPtr.Zero) UnregisterHotKey(_windowHandle, HOTKEY_ID);
            if (_notifyIcon != null) _notifyIcon.Dispose();
            this.Close();
        }
    }
}
