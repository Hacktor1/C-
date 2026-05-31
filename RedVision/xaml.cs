using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Microsoft.Win32;
using MessageBox = System.Windows.MessageBox;
using WpfColor = System.Windows.Media.Color;

namespace RedVision
{
    public partial class MainWindow : Window
    {
        #region Magnification API
        [DllImport("magnification.dll")] public static extern bool MagInitialize();
        [DllImport("magnification.dll")] public static extern bool MagUninitialize();
        [DllImport("magnification.dll")] public static extern bool MagSetFullscreenColorEffect(ref MAGCOLORMATRIX pEffect);
        #endregion

        #region Hotkey API
        [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
        [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
        #endregion

        private const int HOTKEY_ID = 9000;
        private const int WM_HOTKEY = 0x0312;
        private const uint MOD_ALT = 0x0001;
        private const uint MOD_CONTROL = 0x0002;
        private const uint MOD_SHIFT = 0x0004;

        [StructLayout(LayoutKind.Sequential)]
        public struct MAGCOLORMATRIX
        {
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 25)]
            public float[] transform;
        }

        private static readonly MAGCOLORMATRIX RedMatrix = new MAGCOLORMATRIX
        {
            transform = new float[] { 1f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 0f, 0f, 1f }
        };
        private static readonly MAGCOLORMATRIX IdentityMatrix = new MAGCOLORMATRIX
        {
            transform = new float[] { 1f, 0f, 0f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 0f, 0f, 1f }
        };

        private System.Windows.Forms.NotifyIcon _notifyIcon;
        private IntPtr _windowHandle;
        private HwndSource _hwndSource;
        private bool _isExplicitClose = false;
        private bool _isLoadingSettings = true;

        private string _language = "EN";
        private int _activationCount = 0;

        private const string GitHubRepo = "Hacktor1/RedVision";
        private const string GitHubApiLatest = "https://api.github.com/repos/" + GitHubRepo + "/releases/latest";
        private string _downloadUrl = null;
        private string _latestVersion = null;
        private bool _isUpdateAvailable = false;

        private static Mutex _mutex;
        private const string MutexName = "RedVision_SingleInstance_2A4F8C";
        private NamedPipeServerStream _pipeServer;

        private TextBlock _txtMainBtn;

        private static readonly Dictionary<string, Dictionary<string, string>> Loc = new Dictionary<string, Dictionary<string, string>>
        {
            ["EN"] = new Dictionary<string, string>
            {
                ["TrayInfo"] = "Application runs in the background tray.",
                ["Back"] = "⬅ Back",
                ["Settings"] = "Settings",
                ["Interface"] = "Interface & Display",
                ["Language"] = "Language:",
                ["Startup"] = "Run at Windows startup",
                ["Hotkey"] = "Global Hotkey",
                ["ActivationKey"] = "Activation Key:",
                ["Activate"] = "ACTIVATE",
                ["Deactivate"] = "DEACTIVATE",
                ["RedOn"] = "Red Mode ACTIVE",
                ["RedOff"] = "Red Mode disabled",
                ["Install"] = "Install",
                ["UpToDate"] = "Up to date",
                ["Downloading"] = "Downloading...",
                ["Error"] = "Error",
                ["Latest"] = "You have the latest version.",
                ["NoConnection"] = "Could not connect to server.",
                ["EmptyKey"] = "Activation key cannot be empty. Defaulting to 'R'.",
                ["Version"] = "Version 1.2.0",
                ["Stats"] = "Total activations: {0}",
                ["UpdateAvailable"] = "New version {0} available!",
                ["MagInitFail"] = "Failed to initialize magnification API. Application will exit.",
                ["ColorEffectFail"] = "Failed to apply color effect."
            },
            ["CS"] = new Dictionary<string, string>
            {
                ["TrayInfo"] = "Aplikace běží na pozadí v systémové liště.",
                ["Back"] = "⬅ Zpět",
                ["Settings"] = "Nastavení",
                ["Interface"] = "Rozhraní a zobrazení",
                ["Language"] = "Jazyk:",
                ["Startup"] = "Spouštět při startu Windows",
                ["Hotkey"] = "Klávesová zkratka",
                ["ActivationKey"] = "Aktivační klávesa:",
                ["Activate"] = "AKTIVOVAT",
                ["Deactivate"] = "DEAKTIVOVAT",
                ["RedOn"] = "Červený režim AKTIVNÍ",
                ["RedOff"] = "Červený režim vypnut",
                ["Install"] = "Instalovat",
                ["UpToDate"] = "Aktuální",
                ["Downloading"] = "Stahování...",
                ["Error"] = "Chyba",
                ["Latest"] = "Máte nainstalovanou nejnovější verzi.",
                ["NoConnection"] = "Nepodařilo se spojit se serverem.",
                ["EmptyKey"] = "Aktivační klávesa nemůže být prázdná. Nastavuji výchozí 'R'.",
                ["Version"] = "Verze 1.2.0",
                ["Stats"] = "Celkem aktivací: {0}",
                ["UpdateAvailable"] = "Nová verze {0} je k dispozici!",
                ["MagInitFail"] = "Selhala inicializace magnification API. Aplikace bude ukončena.",
                ["ColorEffectFail"] = "Selhalo aplikování barevného efektu."
            }
        };

        public MainWindow()
        {
            _mutex = new Mutex(true, MutexName, out bool createdNew);
            if (!createdNew)
            {
                try
                {
                    using (var client = new NamedPipeClientStream(".", MutexName, PipeDirection.Out))
                    {
                        client.Connect(500);
                        using (var writer = new StreamWriter(client))
                            writer.Write("SHOW");
                    }
                }
                catch { }
                System.Windows.Application.Current.Shutdown();
                return;
            }

            InitializeComponent();

            if (!MagInitialize())
            {
                ShowCustomError(L("MagInitFail"));
                System.Windows.Application.Current.Shutdown();
                return;
            }

            LoadSettings();
            InitSystemTray();
            StartPipeServer();

            Loaded += (s, e) =>
            {
                CacheTemplateParts();
                ApplyLanguage();
                RegisterCustomHotkey();
                _ = CheckForUpdatesAsync(true);
            };
        }

        private void Window_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
                DragMove();
        }

        private void CacheTemplateParts()
        {
            if (BtnToggleRed.Template != null)
                _txtMainBtn = BtnToggleRed.Template.FindName("TxtMainBtn", BtnToggleRed) as TextBlock;
        }

        #region Custom Error Overlay
        private void ShowCustomError(string message, string title = null)
        {
            if (title == null) title = L("Error");
            ErrorTitle.Text = title;
            ErrorMessage.Text = message;

            ErrorOverlay.Visibility = Visibility.Visible;
            var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150));
            ErrorOverlay.BeginAnimation(OpacityProperty, fadeIn);
        }

        private void BtnErrorOk_Click(object sender, RoutedEventArgs e)
        {
            var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(150));
            fadeOut.Completed += (s, a) =>
            {
                ErrorOverlay.Visibility = Visibility.Collapsed;
            };
            ErrorOverlay.BeginAnimation(OpacityProperty, fadeOut);
        }
        #endregion

        #region System Tray & Pipe
        private void InitSystemTray()
        {
            _notifyIcon = new System.Windows.Forms.NotifyIcon
            {
                Icon = System.Drawing.SystemIcons.Application,
                Visible = true,
                Text = "RedVision"
            };
            _notifyIcon.DoubleClick += (s, e) => ShowWindow();

            var menu = new System.Windows.Forms.ContextMenuStrip();
            menu.Items.Add("Open RedVision", null, (s, e) => ShowWindow());
            menu.Items.Add("Exit", null, (s, e) => ExitApplication());
            _notifyIcon.ContextMenuStrip = menu;
        }

        private void StartPipeServer()
        {
            _pipeServer = new NamedPipeServerStream(MutexName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
            _pipeServer.BeginWaitForConnection(OnPipeConnection, null);
        }

        private void OnPipeConnection(IAsyncResult ar)
        {
            try
            {
                _pipeServer.EndWaitForConnection(ar);
                using (var reader = new StreamReader(_pipeServer))
                {
                    string msg = reader.ReadToEnd();
                    if (msg == "SHOW")
                        Dispatcher.Invoke(() => ShowWindow());
                }
            }
            catch { }
            finally
            {
                _pipeServer.Dispose();
                StartPipeServer();
            }
        }

        private void ShowWindow()
        {
            Show();
            WindowState = WindowState.Normal;
            Activate();
        }
        #endregion

        #region Settings
        private string GetConfigPath()
        {
            string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RedVision");
            Directory.CreateDirectory(folder);
            return Path.Combine(folder, "config.txt");
        }

        private void LoadSettings()
        {
            string path = GetConfigPath();
            if (!File.Exists(path)) return;

            try
            {
                foreach (var line in File.ReadAllLines(path))
                {
                    var parts = line.Split('=');
                    if (parts.Length != 2) continue;
                    string key = parts[0], val = parts[1];

                    switch (key)
                    {
                        case "Language": _language = val; break;
                        case "Ctrl": ChkCtrl.IsChecked = bool.Parse(val); break;
                        case "Alt": ChkAlt.IsChecked = bool.Parse(val); break;
                        case "Shift": ChkShift.IsChecked = bool.Parse(val); break;
                        case "Key": TxtKey.Text = val; break;
                        case "Startup": ChkStartup.IsChecked = bool.Parse(val); break;
                        case "Activations": _activationCount = int.Parse(val); break;
                    }
                }
            }
            catch { }

            foreach (ComboBoxItem item in CmbLang.Items)
                if (item.Tag is string tag && tag == _language)
                {
                    CmbLang.SelectedItem = item;
                    break;
                }

            _isLoadingSettings = false;
            SaveSettings();
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
                    $"Key={TxtKey.Text}",
                    $"Startup={ChkStartup.IsChecked}",
                    $"Activations={_activationCount}"
                };
                string path = GetConfigPath();
                string tmp = path + ".tmp";
                File.WriteAllLines(tmp, lines);
                File.Replace(tmp, path, null);
            }
            catch { }
        }

        private void ChkStartup_Changed(object sender, RoutedEventArgs e)
        {
            ApplyStartupSetting();
            SaveSettings();
        }

        private void ApplyStartupSetting()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true))
                {
                    if (ChkStartup.IsChecked == true)
                        key.SetValue("RedVision", Process.GetCurrentProcess().MainModule.FileName);
                    else
                        key.DeleteValue("RedVision", false);
                }
            }
            catch { }
        }
        #endregion

        #region Language & UI
        private void CmbLang_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CmbLang.SelectedItem is ComboBoxItem item && item.Tag is string tag)
            {
                _language = tag;
                ApplyLanguage();
                SaveSettings();
            }
        }

        private string L(string key)
        {
            if (Loc.TryGetValue(_language, out var dict) && dict.TryGetValue(key, out var val))
                return val;
            if (Loc.TryGetValue("EN", out var enDict) && enDict.TryGetValue(key, out var enVal))
                return enVal;
            return key;
        }

        private void ApplyLanguage()
        {
            if (!IsInitialized) return;

            TxtTrayInfo.Text = L("TrayInfo");
            BtnBack.Content = L("Back");
            TxtSettingsTitle.Text = L("Settings");
            TxtDisplayCategory.Text = L("Interface");
            TxtLangLabel.Text = L("Language");
            ChkStartup.Content = L("Startup");
            TxtHotkeyCategory.Text = L("Hotkey");
            TxtKeyLabel.Text = L("ActivationKey");
            TxtStats.Text = string.Format(L("Stats"), _activationCount);

            UpdateToggleText(BtnToggleRed.IsChecked == true);
            SetUpdateButtonUI();
        }

        private void UpdateToggleText(bool isActive)
        {
            if (_txtMainBtn != null)
                _txtMainBtn.Text = isActive ? L("Deactivate") : L("Activate");

            TxtStatus.Text = isActive ? L("RedOn") : L("RedOff");
            TxtStatus.Foreground = new SolidColorBrush(isActive ?
                WpfColor.FromRgb(255, 69, 58) : WpfColor.FromRgb(170, 170, 170));
        }

        private void SetUpdateButtonUI()
        {
            if (BtnUpdate == null) return;
            string text = _isUpdateAvailable ? L("Install") : L("UpToDate");
            byte r = _isUpdateAvailable ? (byte)48 : (byte)80;
            byte g = _isUpdateAvailable ? (byte)209 : (byte)80;
            byte b = _isUpdateAvailable ? (byte)88 : (byte)80;
            BtnUpdate.Content = $"⬇ {text}";
            BtnUpdate.Background = new SolidColorBrush(WpfColor.FromRgb(r, g, b));
        }
        #endregion

        #region Hotkey
        private void HotkeyChanged(object sender, RoutedEventArgs e)
        {
            RegisterCustomHotkey();
            SaveSettings();
        }

        private void TxtKey_TextChanged(object sender, TextChangedEventArgs e) { }

        private void TxtKey_LostFocus(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(TxtKey.Text))
            {
                TxtKey.Text = "R";
                ShowCustomError(L("EmptyKey"));
            }
            RegisterCustomHotkey();
            SaveSettings();
        }

        private void RegisterCustomHotkey()
        {
            if (_windowHandle == IntPtr.Zero) return;
            UnregisterHotKey(_windowHandle, HOTKEY_ID);

            uint modifiers = 0;
            if (ChkCtrl.IsChecked == true) modifiers |= MOD_CONTROL;
            if (ChkAlt.IsChecked == true) modifiers |= MOD_ALT;
            if (ChkShift.IsChecked == true) modifiers |= MOD_SHIFT;

            string keyText = TxtKey.Text?.Trim().ToUpper();
            if (string.IsNullOrEmpty(keyText)) return;

            if (Enum.TryParse(keyText, out Key parsedKey))
            {
                uint vk = (uint)KeyInterop.VirtualKeyFromKey(parsedKey);
                RegisterHotKey(_windowHandle, HOTKEY_ID, modifiers, vk);
            }
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            _windowHandle = new WindowInteropHelper(this).Handle;
            _hwndSource = HwndSource.FromHwnd(_windowHandle);
            _hwndSource.AddHook(HwndHook);
            RegisterCustomHotkey();
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
        #endregion

        #region Filter & Panels
        private void BtnToggleRed_Click(object sender, RoutedEventArgs e) => ApplyColorFilter();

        private void ApplyColorFilter()
        {
            bool isActive = BtnToggleRed.IsChecked == true;

            MAGCOLORMATRIX matrix;
            if (isActive)
                matrix = new MAGCOLORMATRIX { transform = (float[])RedMatrix.transform.Clone() };
            else
                matrix = new MAGCOLORMATRIX { transform = (float[])IdentityMatrix.transform.Clone() };

            if (!MagSetFullscreenColorEffect(ref matrix))
            {
                ShowCustomError(L("ColorEffectFail"));
                BtnToggleRed.IsChecked = !isActive;
                return;
            }

            if (isActive)
            {
                _activationCount++;
                SaveSettings();
                TxtStats.Text = string.Format(L("Stats"), _activationCount);
            }

            StateDot.Fill = new SolidColorBrush(isActive ?
                WpfColor.FromRgb(48, 209, 88) : WpfColor.FromRgb(255, 69, 58));

            UpdateToggleText(isActive);
        }

        private void BtnSettings_Click(object sender, RoutedEventArgs e) => SwitchPanels(MainPanel, SettingsPanel);
        private void BtnBack_Click(object sender, RoutedEventArgs e) => SwitchPanels(SettingsPanel, MainPanel);

        private void SwitchPanels(UIElement hidePanel, UIElement showPanel)
        {
            var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(150));
            fadeOut.Completed += (s, e) =>
            {
                hidePanel.Visibility = Visibility.Collapsed;
                showPanel.Visibility = Visibility.Visible;
                var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150));
                showPanel.BeginAnimation(OpacityProperty, fadeIn);
            };
            hidePanel.BeginAnimation(OpacityProperty, fadeOut);
        }
        #endregion

        #region GitHub Updates
        private async Task CheckForUpdatesAsync(bool isAutomatic)
        {
            try
            {
                using (var client = new HttpClient())
                {
                    client.DefaultRequestHeaders.UserAgent.ParseAdd("RedVision-Updater");
                    client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github.v3+json");
                    var response = await client.GetStringAsync(GitHubApiLatest);

                    using (JsonDocument doc = JsonDocument.Parse(response))
                    {
                        var root = doc.RootElement;
                        string tag = root.GetProperty("tag_name").GetString();
                        _latestVersion = tag?.StartsWith("v") == true ? tag.Substring(1) : tag;

                        if (Version.TryParse(_latestVersion, out Version online) && Version.TryParse("1.2.0", out Version current))
                        {
                            if (online > current)
                            {
                                _isUpdateAvailable = true;
                                if (root.TryGetProperty("assets", out JsonElement assets))
                                {
                                    foreach (JsonElement asset in assets.EnumerateArray())
                                    {
                                        string name = asset.GetProperty("name").GetString();
                                        if (name != null && name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                                        {
                                            _downloadUrl = asset.GetProperty("browser_download_url").GetString();
                                            break;
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch { }

            _isUpdateAvailable = _isUpdateAvailable && !string.IsNullOrEmpty(_downloadUrl);
            SetUpdateButtonUI();

            if (!isAutomatic)
            {
                string msg = _isUpdateAvailable
                    ? string.Format(L("UpdateAvailable"), _latestVersion)
                    : L("Latest");
                ShowCustomError(msg);  // použijeme vlastní hlášku
            }
        }

        private async void BtnUpdate_Click(object sender, RoutedEventArgs e)
        {
            if (!_isUpdateAvailable)
            {
                BtnUpdate.IsEnabled = false;
                await CheckForUpdatesAsync(false);
                BtnUpdate.IsEnabled = true;
                return;
            }

            BtnUpdate.IsEnabled = false;
            string downloading = L("Downloading");
            BtnUpdate.Content = $"⏳ {downloading}";
            BtnUpdate.Background = new SolidColorBrush(WpfColor.FromRgb(255, 159, 10));

            try
            {
                using (var client = new HttpClient())
                {
                    var data = await client.GetByteArrayAsync(_downloadUrl);
                    string exePath = Process.GetCurrentProcess().MainModule.FileName;
                    string newFile = exePath + ".new";
                    File.WriteAllBytes(newFile, data);

                    string bat = Path.Combine(Path.GetDirectoryName(exePath), "updater.bat");
                    string batContent = $@"
@echo off
timeout /t 2 /nobreak > NUL
move /y ""{newFile}"" ""{exePath}""
start """" ""{exePath}""
del ""%~f0""
";
                    File.WriteAllText(bat, batContent);
                    Process.Start(new ProcessStartInfo { FileName = bat, CreateNoWindow = true, UseShellExecute = false });
                    ExitApplication();
                }
            }
            catch (Exception ex)
            {
                ShowCustomError($"Update error: {ex.Message}");
                BtnUpdate.IsEnabled = true;
                SetUpdateButtonUI();
            }
        }
        #endregion

        #region Window Close & Exit
        private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();

        protected override void OnClosing(CancelEventArgs e)
        {
            if (!_isExplicitClose)
            {
                e.Cancel = true;
                Hide();
            }
            base.OnClosing(e);
        }

        private void ExitApplication()
        {
            _isExplicitClose = true;
            var idMatrix = new MAGCOLORMATRIX { transform = (float[])IdentityMatrix.transform.Clone() };
            MagSetFullscreenColorEffect(ref idMatrix);
            MagUninitialize();
            if (_windowHandle != IntPtr.Zero) UnregisterHotKey(_windowHandle, HOTKEY_ID);
            _notifyIcon?.Dispose();
            _pipeServer?.Dispose();
            _mutex?.Dispose();
            Close();
        }
        #endregion
    }
}
