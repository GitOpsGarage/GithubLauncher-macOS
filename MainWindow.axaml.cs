using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GitHubLauncher.Core.Models;
using GitHubLauncher.Core.Services;
using AsyncImageLoader;
using GithubLauncher.Models;
using GithubLauncher.Services;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Runtime.InteropServices;
using Avalonia.Platform;
using Avalonia.Markup.Xaml.Converters;


#if WINDOWS
using NAudio.Wave;
#endif

namespace GithubLauncher
{
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        private readonly GameManager _gameManager;
        public ObservableCollection<GameInfo> Games => _gameManager?.Games ?? new ObservableCollection<GameInfo>();
        public AppSettings _settings = new();
        public App _app = null!;
        public AppSettings Settings => _settings;
        private bool isSettingsPanelOpen = false;
        public string IconFillStretch = "Uniform";
        private string _backgroundImagePath = string.Empty;
        private string _backgroundImageUri = string.Empty;
        public string BackgroundImagePath
        {
            get => _backgroundImagePath;
            set
            {
                if (_backgroundImagePath != value)
                {
                    _backgroundImagePath = value;
                    if (!string.IsNullOrEmpty(value) && File.Exists(value))
                    {
                        _backgroundImageUri = new Uri(value).AbsoluteUri;
                    }
                    else
                    {
                        _backgroundImageUri = string.Empty;
                    }
                    OnPropertyChanged(nameof(BackgroundImagePath));
                    OnPropertyChanged(nameof(BackgroundImageUri));
                }
            }
        }
        public string BackgroundImageUri => _backgroundImageUri ?? string.Empty;
        public float BackgroundOpacity
        {
            get => _settings.BackgroundOpacity;
            set
            {
                if (Math.Abs(_settings.BackgroundOpacity - value) > 0.001f)
                {
                    _settings.BackgroundOpacity = value;
                    OnPropertyChanged(nameof(BackgroundOpacity));
                }
            }
        }
        private System.Threading.CancellationTokenSource? _fadeTaskCts;
        private const int FADE_DURATION_MS = 500;
        #if WINDOWS
        private IWavePlayer? _waveOut;
        private AudioFileReader? _audioFileReader;
        #endif
        private Process? _musicProcess;
        private bool _musicPausedByDeactivation = false;
        private bool _launchedGameOwnsInput;
        private bool _trackingLaunchedGameProcess;
        private string _launcherMusicPath = string.Empty;
        public string LauncherMusicPath
        {
            get => _launcherMusicPath;
            set
            {
                if (_launcherMusicPath != value)
                {
                    _launcherMusicPath = value;
                    OnPropertyChanged(nameof(LauncherMusicPath));
                }
            }
        }
        private float _musicVolume = 0.2f;
        public float MusicVolume
        {
            get => _musicVolume;
            set
            {
                if (Math.Abs(_musicVolume - value) > 0.001f)
                {
                    _musicVolume = value;
                    OnPropertyChanged(nameof(MusicVolume));

                #if WINDOWS
                if (_audioFileReader != null)
                {
                    _audioFileReader.Volume = value;
                }
                #else
                if (!string.IsNullOrEmpty(LauncherMusicPath) && File.Exists(LauncherMusicPath))
                {
                    PlayLauncherMusic(LauncherMusicPath);
                }
                #endif
                }
            }
        }
        public bool IsFullscreen
        {
            get => _settings.StartFullscreen;
            set
            {
                if (_settings.StartFullscreen != value)
                {
                    _settings.StartFullscreen = value;
                    OnPropertyChanged(nameof(IsFullscreen));
                }
            }
        }
        public bool CloseAfterLaunch
        {
            get => _settings.CloseAfterLaunch;
            set
            {
                if (_settings.CloseAfterLaunch != value)
                {
                    _settings.CloseAfterLaunch = value;
                    OnPropertyChanged(nameof(CloseAfterLaunch));
                }
            }
        }
        public IBrush WindowBackground
        {
            get
            {
                if (_settings?.WindowBorderRounding ?? false)
                {
                    if (IsFullscreen)
                    {
                        return this.Resources["ThemeDarker"] as IBrush ?? Brushes.Transparent;
                    }
                    if (_settings.ShowOSTopBar)
                    {
                        return this.Resources["ThemeDarker"] as IBrush ?? Brushes.Transparent;
                    }
                    return Brushes.Transparent;
                }
                return this.Resources["ThemeDarker"] as IBrush ?? Brushes.Transparent;
            }
        }
        public bool ExtendClientAreaEnabled => !_settings.ShowOSTopBar;
        public ExtendClientAreaChromeHints ChromeHints
        {
            get => _settings.ShowOSTopBar ? ExtendClientAreaChromeHints.PreferSystemChrome : ExtendClientAreaChromeHints.NoChrome;
        }
        private string _currentSortBy = "Name";
        private string _currentVersionString = string.Empty;
        public string currentVersionString
        {
            get => _currentVersionString;
            set
            {
                if (_currentVersionString != value)
                {
                    _currentVersionString = value;
                    OnPropertyChanged(nameof(currentVersionString));
                }
            }
        }
        private string _platformstring = string.Empty;
        public string PlatformString
        {             
            get => _platformstring;
            set
            {
                if (_platformstring != value)
                {
                    _platformstring = value;
                    OnPropertyChanged(nameof(PlatformString));
                    OnPropertyChanged(nameof(IsLinuxPlatform));
                }
            }
        }
        public bool IsLinuxPlatform => PlatformString.Contains("Linux", StringComparison.OrdinalIgnoreCase);
        public bool IsMacPlatform => OperatingSystem.IsMacOS();

        private bool _isContinueVisible;
        public bool IsContinueVisible
        {
            get => _isContinueVisible;
            set
            {
                if (_isContinueVisible != value)
                {
                    _isContinueVisible = value;
                    OnPropertyChanged(nameof(IsContinueVisible));
                }
            }
        }

        private InputService? _inputService;
        private bool _isProcessingInput = false;
        private bool _hasInitializedFocus = false;

        private bool _isChangelogOpen = false;
        private GameInfo? _currentChangelogGame;

        private GameInfo? _continueGameInfo;
        public GameInfo? ContinueGameInfo
        {
            get => _continueGameInfo;
            set
            {
                if (_continueGameInfo != value)
                {
                    _continueGameInfo = value;
                    OnPropertyChanged(nameof(ContinueGameInfo));
                }
            }
        }
        private bool _isGamesManagerOpen = false;
        public string InfoTextLength = "*";
        private SolidColorBrush _themeColorBrush = new(Colors.Transparent);
        public SolidColorBrush ThemeColorBrush
        {
            get => _themeColorBrush;
            set
            {
                if (_themeColorBrush != value)
                {
                    _themeColorBrush = value;
                    OnPropertyChanged(nameof(ThemeColorBrush));
                    UpdateThemeColors();
                }
            }
        }
        private SolidColorBrush _secondaryColorBrush = new(Colors.Transparent);
        public SolidColorBrush SecondaryColorBrush
        {
            get => _secondaryColorBrush;
            set
            {
                if (_secondaryColorBrush != value)
                {
                    _secondaryColorBrush = value;
                    OnPropertyChanged(nameof(SecondaryColorBrush));
                    UpdateThemeColors();
                }
            }
        }

        public MainWindow()
        {
            InitializeComponent();

            try
            {
                _settings = AppSettings.Load();
            }
            catch (Exception ex)
            {
                _ = ShowMessageBoxAsync($"Failed to load settings: {ex.Message}", "Settings Error");
                _settings = new AppSettings();
            }

            _gameManager = new GameManager();

            // Initialize theme
            ThemeColorBrush = new SolidColorBrush(Color.Parse(_settings?.PrimaryColor ?? "#18181b"));
            SecondaryColorBrush = new SolidColorBrush(Color.Parse(_settings?.SecondaryColor ?? "#404040"));
            UpdateThemeColors();

            _gameManager.UnhideAllGames();
            LoadCurrentVersion();
            LoadCurrentPlatform();
            UpdateSettingsUI();

            // Apply fullscreen from settings immediately
            if (_settings.StartFullscreen)
            {
                WindowState = WindowState.FullScreen;
            }

            // Initialize background image from settings
            BackgroundImagePath = _settings.BackgroundImagePath ?? string.Empty;

            // Initialize music from settings
            LauncherMusicPath = _settings.LauncherMusicPath ?? string.Empty;
            MusicVolume = _settings.MusicVolume;
            if (!string.IsNullOrEmpty(LauncherMusicPath) && File.Exists(LauncherMusicPath))
            {
                PlayLauncherMusic(LauncherMusicPath);
            }

            _inputService = new InputService(this, _settings);
            _inputService.OnConfirm += HandleConfirmAction;
            _inputService.OnCancel += HandleCancelAction;

            this.KeyDown += MainWindow_KeyDown;
            this.KeyUp += MainWindow_KeyUp;

            this.Activated += MainWindow_Activated;
            this.Deactivated += MainWindow_Deactivated;

            _gameManager.PropertyChanged += (s, e) => {
                if (e.PropertyName == nameof(GameManager.Games))
                {
                    OnPropertyChanged(nameof(Games));
                    UpdateContinueButtonState();
                    Debug.WriteLine($"Games collection changed. Count: {_gameManager.Games?.Count ?? 0}");
                    foreach (var game in _gameManager.Games ?? new())
                    {
                        SubscribeToGameEvents(game);
                        Debug.WriteLine($"Game: {game.Name}, IconUrl: {game.IconUrl}");
                    }
                }
            };
        }

        // Is Theme Color Light
        private bool IsLightColor(Color color)
        {
            // Calculate perceived brightness using standard formula
            double brightness = (0.299 * color.R + 0.587 * color.G + 0.114 * color.B) / 255;
            return brightness > 0.5;
        }

        // Theme Color Shades
        private Color GetShadedColor(Color baseColor, double factor)
        {
            byte r = (byte)Math.Min(255, Math.Max(0, baseColor.R * factor));
            byte g = (byte)Math.Min(255, Math.Max(0, baseColor.G * factor));
            byte b = (byte)Math.Min(255, Math.Max(0, baseColor.B * factor));
            return Color.FromRgb(r, g, b);
        }

        private void UpdateThemeColors()
        {
            if (_themeColorBrush == null || _secondaryColorBrush == null) return;

            var primaryColor = _themeColorBrush.Color;
            var secondaryColor = _secondaryColorBrush.Color;
            var themeBase = new SolidColorBrush(primaryColor);
            var themeLighter = new SolidColorBrush(GetShadedColor(primaryColor, 1.3));
            var themeDarker = new SolidColorBrush(GetShadedColor(primaryColor, 0.7));
            var themeBorder = new SolidColorBrush(secondaryColor);

            var textColor = CalculateLuminance(primaryColor) > 0.5 ? Colors.Black : Colors.White;
            var tintedText = new SolidColorBrush(BlendColors(textColor, secondaryColor, 0.08));
            var tintedTextSecondary = new SolidColorBrush(
                CalculateLuminance(primaryColor) > 0.5
                    ? BlendColors(Color.FromRgb(70, 70, 70), secondaryColor, 0.15)
                    : BlendColors(Color.FromRgb(200, 200, 200), secondaryColor, 0.15)
            );

            Resources["ThemeBase"] = themeBase;
            Resources["ThemeLighter"] = themeLighter;
            Resources["ThemeDarker"] = themeDarker;
            Resources["ThemeBorder"] = themeBorder;
            Resources["ThemeText"] = tintedText;
            Resources["ThemeTextSecondary"] = tintedTextSecondary;

            OnPropertyChanged(nameof(WindowBackground));
        }

        private double CalculateLuminance(Color color)
        {
            return (0.299 * color.R + 0.587 * color.G + 0.114 * color.B) / 255;
        }

        // Color Picker Preset Dialog
        private async void ThemeColorPicker_Click(object sender, RoutedEventArgs e)
        {
            // Simple color presets dialog
            var presets = new Dictionary<string, string>
            {
                { "Black", "#000000" },
                { "Darker Gray", "#101010" },
                { "Dark Gray (Default)", "#18181b" },
                { "Charcoal Gray", "#2c2c2c" },
                { "Slate Gray", "#36454f" },

                { "Dark Navy Blue", "#1e3a5f" },
                { "Deep Navy", "#0f2b46" },
                { "Deep Indigo", "#2c3e50" },
                { "Dark Grayish Blue", "#45475a" },
                { "Midnight Black", "#19191c" },

                { "Deep Forest Green", "#1a4d2e" },
                { "Darkest Green", "#063204" },
                { "Forest Green", "#228b22" },
                { "Deep Moss Green", "#2c5f2d" },
                { "Deep Forest", "#134411" },
                { "Black Forest Green", "#051D01" },
                { "Dark Olive Green", "#556b2f" },
                { "Dark Olive Drab", "#2a2922" },

                { "Deep Purple", "#2d1b4e" },
                { "Deep Plum", "#4b0082" },
                { "Dark Eggplant", "#614051" },

                { "Dark Burgundy", "#4d1f1f" },
                { "Burgundy", "#800020" },
                { "Deep Maroon", "#5c0b0b" },

                { "Light Gray", "#e5e5e5" },
                { "Silver Gray", "#c0c0c0" },
                { "Pale Gray", "#f0f0f0" },

                { "Soft Blue", "#d4e4f7" },
                { "Sky Blue", "#87ceeb" },
                { "Powder Blue", "#b0e0e6" },

                { "Seafoam Green", "#d4f1e8" },
                { "Mint Green", "#98fb98" },
                { "Bright Sea Foam", "#98ff98" }
            };

            await ShowColorPresetsDialog(presets);
        }

        // Secondary Color Picker
        private async void SecondaryColorPicker_Click(object sender, RoutedEventArgs e)
        {
            // Simple color presets dialog
            var presets = new Dictionary<string, string>
            {
                { "Black", "#000000" },
                { "Dark Gray (Default)", "#404040" },
                { "Gray", "#737373" },
                { "Light Gray", "#d4d4d4" },
                { "White", "#ffffff" },

                { "Red", "#ef4444" },
                { "Orange", "#f97316" },
                { "Yellow", "#eab308" },
                { "Lime", "#84cc16" },
                { "Green", "#10b981" },
                { "Teal", "#14b8a6" },
                { "Cyan", "#06b6d4" },
                { "Sky Blue", "#0ea5e9" },
                { "Blue", "#3b82f6" },
                { "Purple", "#a855f7" },
                { "Violet", "#8b5cf6" },
                { "Indigo", "#6366f1" },
                { "Pink", "#ec4899" } 
            };

            await ShowColorPresetsDialog(presets, true);
        }

        private Color BlendColors(Color baseColor, Color blendColor, double blendAmount)
        {
            byte r = (byte)(baseColor.R * (1 - blendAmount) + blendColor.R * blendAmount);
            byte g = (byte)(baseColor.G * (1 - blendAmount) + blendColor.G * blendAmount);
            byte b = (byte)(baseColor.B * (1 - blendAmount) + blendColor.B * blendAmount);
            return Color.FromRgb(r, g, b);
        }

        private async Task ShowColorPresetsDialog(Dictionary<string, string> presets, bool isSecondary = false)
        {
            await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop &&
                    desktop.MainWindow != null)
                {
                    var stackPanel = new StackPanel { Margin = new Thickness(20), Spacing = 10 };

                    // Add Custom Color button at the top
                    var customButton = new Button
                    {
                        Content = "🎨 Custom Color Picker",
                        HorizontalAlignment = HorizontalAlignment.Stretch,
                        Height = 50,
                        Background = new SolidColorBrush(Color.FromRgb(50, 50, 50)),
                        Foreground = new SolidColorBrush(Colors.White),
                        FontWeight = FontWeight.Bold
                    };

                    customButton.Click += async (s, e) =>
                    {
                        // Close presets dialog
                        var window = (s as Button)?.GetVisualRoot() as Window;
                        window?.Close();

                        // Open custom color picker
                        await ShowCustomColorPicker(isSecondary);
                    };

                    stackPanel.Children.Add(customButton);

                    // Add separator
                    stackPanel.Children.Add(new Border
                    {
                        Height = 1,
                        Background = new SolidColorBrush(Color.FromRgb(100, 100, 100)),
                        Margin = new Thickness(0, 5, 0, 5)
                    });

                    foreach (var preset in presets)
                    {
                        var button = new Button
                        {
                            Content = preset.Key,
                            HorizontalAlignment = HorizontalAlignment.Stretch,
                            Height = 40,
                            Background = new SolidColorBrush(Color.Parse(preset.Value)),
                            Foreground = new SolidColorBrush(IsLightColor(Color.Parse(preset.Value)) ? Colors.Black : Colors.White),
                            Tag = preset.Value
                        };

                        button.Click += (s, e) =>
                        {
                            var colorHex = (s as Button)?.Tag as string;
                            if (!string.IsNullOrEmpty(colorHex))
                            {
                                if (isSecondary)
                                {
                                    _settings.SecondaryColor = colorHex;
                                    SecondaryColorBrush = new SolidColorBrush(Color.Parse(colorHex));
                                }
                                else
                                {
                                    _settings.PrimaryColor = colorHex;
                                    ThemeColorBrush = new SolidColorBrush(Color.Parse(colorHex));
                                }
                                OnSettingChanged();

                                // Close the dialog after selection
                                if (s is Button btn && btn.Parent != null)
                                {
                                    var window = btn.GetVisualRoot() as Window;
                                    window?.Close();
                                }
                            }
                        };

                        stackPanel.Children.Add(button);
                    }

                    var messageBox = new Window
                    {
                        Title = isSecondary ? "Select Secondary Color" : "Select Primary Color",
                        Width = 300,
                        Height = 1000,
                        WindowStartupLocation = WindowStartupLocation.CenterOwner,
                        Content = new ScrollViewer { Content = stackPanel }
                    };

                    await messageBox.ShowDialog(desktop.MainWindow);
                }
            });
        }

        private async Task ShowCustomColorPicker(bool isSecondary = false)
        {
            await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop &&
                    desktop.MainWindow != null)
                {
                    var currentColor = isSecondary ? SecondaryColorBrush.Color : ThemeColorBrush.Color;
                    var (h, s, l) = RgbToHsl(currentColor);

                    var pickerPanel = new StackPanel { Margin = new Thickness(20), Spacing = 15 };

                    // Preview box
                    var previewBorder = new Border
                    {
                        Width = 260,
                        Height = 60,
                        CornerRadius = new CornerRadius(8),
                        Background = new SolidColorBrush(currentColor),
                        BorderBrush = new SolidColorBrush(Colors.White),
                        BorderThickness = new Thickness(2)
                    };
                    pickerPanel.Children.Add(previewBorder);

                    // HSL Sliders
                    var hSlider = CreateHslSlider("Hue", h, 0, 360, "°");
                    var sSlider = CreateHslSlider("Saturation", s, 0, 100, "%");
                    var lSlider = CreateHslSlider("Lightness", l, 0, 100, "%");

                    pickerPanel.Children.Add(hSlider.panel);
                    pickerPanel.Children.Add(sSlider.panel);
                    pickerPanel.Children.Add(lSlider.panel);

                    // Update preview on slider change
                    EventHandler<AvaloniaPropertyChangedEventArgs> updatePreview = (s, e) =>
                    {
                        var newColor = HslToRgb(hSlider.slider.Value, sSlider.slider.Value, lSlider.slider.Value);
                        previewBorder.Background = new SolidColorBrush(newColor);
                    };

                    hSlider.slider.PropertyChanged += updatePreview;
                    sSlider.slider.PropertyChanged += updatePreview;
                    lSlider.slider.PropertyChanged += updatePreview;

                    // Hex input
                    var hexPanel = new StackPanel { Spacing = 5 };
                    hexPanel.Children.Add(new TextBlock
                    {
                        Text = "Hex Color",
                        Foreground = new SolidColorBrush(Colors.White),
                        FontSize = 12
                    });

                    var hexBox = new TextBox
                    {
                        Text = $"#{currentColor.R:X2}{currentColor.G:X2}{currentColor.B:X2}",
                        Watermark = "#RRGGBB",
                        Foreground = new SolidColorBrush(Colors.White),
                        Background = new SolidColorBrush(Color.FromRgb(40, 40, 40))
                    };

                    hexBox.TextChanged += (s, e) =>
                    {
                        try
                        {
                            var text = hexBox.Text?.Trim();
                            if (!string.IsNullOrEmpty(text) && text.StartsWith("#") && text.Length == 7)
                            {
                                var color = Color.Parse(text);
                                var (hue, sat, light) = RgbToHsl(color);
                                hSlider.slider.Value = hue;
                                sSlider.slider.Value = sat;
                                lSlider.slider.Value = light;
                            }
                        }
                        catch { }
                    };

                    // Update hex box when sliders change
                    EventHandler<AvaloniaPropertyChangedEventArgs> updateHex = (s, e) =>
                    {
                        var color = HslToRgb(hSlider.slider.Value, sSlider.slider.Value, lSlider.slider.Value);
                        hexBox.Text = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
                    };

                    hSlider.slider.PropertyChanged += updateHex;
                    sSlider.slider.PropertyChanged += updateHex;
                    lSlider.slider.PropertyChanged += updateHex;

                    hexPanel.Children.Add(hexBox);
                    pickerPanel.Children.Add(hexPanel);

                    // Buttons
                    var buttonPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Margin = new Thickness(0, 10, 0, 0) };

                    var applyButton = new Button
                    {
                        Content = "Apply",
                        Width = 120,
                        Height = 35,
                        Background = new SolidColorBrush(Color.FromRgb(34, 197, 94)),
                        Foreground = new SolidColorBrush(Colors.White)
                    };

                    var cancelButton = new Button
                    {
                        Content = "Cancel",
                        Width = 120,
                        Height = 35,
                        Background = new SolidColorBrush(Color.FromRgb(100, 100, 100)),
                        Foreground = new SolidColorBrush(Colors.White)
                    };

                    buttonPanel.Children.Add(applyButton);
                    buttonPanel.Children.Add(cancelButton);
                    pickerPanel.Children.Add(buttonPanel);

                    var pickerWindow = new Window
                    {
                        Title = isSecondary ? "Custom Secondary Color" : "Custom Primary Color",
                        Width = 320,
                        Height = 480,
                        WindowStartupLocation = WindowStartupLocation.CenterOwner,
                        Background = new SolidColorBrush(Color.FromRgb(30, 30, 30)),
                        Content = pickerPanel,
                        CanResize = false
                    };

                    applyButton.Click += (s, e) =>
                    {
                        var finalColor = HslToRgb(hSlider.slider.Value, sSlider.slider.Value, lSlider.slider.Value);
                        var hexColor = $"#{finalColor.R:X2}{finalColor.G:X2}{finalColor.B:X2}";

                        if (isSecondary)
                        {
                            _settings.SecondaryColor = hexColor;
                            SecondaryColorBrush = new SolidColorBrush(finalColor);
                        }
                        else
                        {
                            _settings.PrimaryColor = hexColor;
                            ThemeColorBrush = new SolidColorBrush(finalColor);
                        }
                        OnSettingChanged();
                        pickerWindow.Close();
                    };

                    cancelButton.Click += (s, e) => pickerWindow.Close();

                    await pickerWindow.ShowDialog(desktop.MainWindow);
                }
            });
        }

        private (StackPanel panel, Slider slider) CreateHslSlider(string label, double initialValue, double min, double max, string unit)
        {
            var panel = new StackPanel { Spacing = 5 };

            var headerPanel = new StackPanel { Orientation = Orientation.Horizontal };
            headerPanel.Children.Add(new TextBlock
            {
                Text = label,
                Foreground = new SolidColorBrush(Colors.White),
                FontSize = 12,
                Width = 80
            });

            var valueText = new TextBlock
            {
                Text = $"{(int)initialValue}{unit}",
                Foreground = new SolidColorBrush(Colors.White),
                FontSize = 12,
                Width = 50,
                TextAlignment = TextAlignment.Right
            };
            headerPanel.Children.Add(valueText);

            panel.Children.Add(headerPanel);

            var slider = new Slider
            {
                Minimum = min,
                Maximum = max,
                Value = initialValue,
                Width = 260,
                TickFrequency = 1,
                IsSnapToTickEnabled = true
            };

            slider.PropertyChanged += (s, e) =>
            {
                if (e.Property.Name == "Value")
                {
                    valueText.Text = $"{(int)slider.Value}{unit}";
                }
            };

            panel.Children.Add(slider);

            return (panel, slider);
        }

        // Convert RGB to HSL
        private (double h, double s, double l) RgbToHsl(Color color)
        {
            double r = color.R / 255.0;
            double g = color.G / 255.0;
            double b = color.B / 255.0;

            double max = Math.Max(r, Math.Max(g, b));
            double min = Math.Min(r, Math.Min(g, b));
            double delta = max - min;

            double h = 0;
            double s = 0;
            double l = (max + min) / 2.0;

            if (delta != 0)
            {
                s = l > 0.5 ? delta / (2.0 - max - min) : delta / (max + min);

                if (max == r)
                    h = ((g - b) / delta + (g < b ? 6 : 0)) / 6.0;
                else if (max == g)
                    h = ((b - r) / delta + 2) / 6.0;
                else
                    h = ((r - g) / delta + 4) / 6.0;
            }

            return (h * 360, s * 100, l * 100);
        }

        // Convert HSL to RGB
        private Color HslToRgb(double h, double s, double l)
        {
            h = h / 360.0;
            s = s / 100.0;
            l = l / 100.0;

            double r, g, b;

            if (s == 0)
            {
                r = g = b = l;
            }
            else
            {
                double q = l < 0.5 ? l * (1 + s) : l + s - l * s;
                double p = 2 * l - q;

                r = HueToRgb(p, q, h + 1.0 / 3.0);
                g = HueToRgb(p, q, h);
                b = HueToRgb(p, q, h - 1.0 / 3.0);
            }

            return Color.FromRgb(
                (byte)Math.Round(r * 255),
                (byte)Math.Round(g * 255),
                (byte)Math.Round(b * 255)
            );
        }

        private double HueToRgb(double p, double q, double t)
        {
            if (t < 0) t += 1;
            if (t > 1) t -= 1;
            if (t < 1.0 / 6.0) return p + (q - p) * 6 * t;
            if (t < 1.0 / 2.0) return q;
            if (t < 2.0 / 3.0) return p + (q - p) * (2.0 / 3.0 - t) * 6;
            return p;
        }

        private void UpdateContinueButtonState()
        {
            ContinueGameInfo = _gameManager.GetLatestPlayedInstalledGame();
            IsContinueVisible = ContinueGameInfo != null;
        }

        private void TopBar_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            // Skip if Hovering ComboBox or if system decorations are enabled
            var source = e.Source as Control;
            if (IsDescendantOf(source, SortByComboBox))
            {
                return;
            }
            if (_settings.ShowOSTopBar)
            {
                return;
            }

            var point = e.GetCurrentPoint(this);

            if (point.Properties.IsLeftButtonPressed)
            {
                if (e.ClickCount == 2)
                {
                    // Double-click to maximize/restore
                    WindowState = WindowState == WindowState.Maximized
                        ? WindowState.Normal
                        : WindowState.Maximized;
                }
                else
                {
                    // Single-click drag to move
                    BeginMoveDrag(e);
                }
            }
        }

        private bool IsDescendantOf(Control? control, Control? parent)
        {
            if (control == null || parent == null)
                return false;

            while (control != null)
            {
                if (control == parent)
                    return true;
                control = control.Parent as Control;
            }

            return false;
        }

        private void LoadCurrentPlatform()
        {
            if (_settings != null)
            {
                PlatformString = _settings.Platform switch
                {
                    TargetOS.Auto => "Automatic",
                    TargetOS.Windows => "Windows",
                    TargetOS.MacOS => "macOS",
                    TargetOS.LinuxX64 => "Linux x64",
                    TargetOS.LinuxARM64 => "Linux ARM64",
                    _ => "Unknown"
                };
            }
            else
            {
                PlatformString = "Unknown";
            }
        }

        private void LoadCurrentVersion()
        {
            try
            {
                string currentAppDirectory = AppDomain.CurrentDomain.BaseDirectory;
                string updateCheckFilePath = Path.Combine(currentAppDirectory, "update_check.json");

                if (File.Exists(updateCheckFilePath))
                {
                    try
                    {
                        var json = File.ReadAllText(updateCheckFilePath);
                        var updateInfo = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);
                        if (updateInfo != null && updateInfo.TryGetValue("CurrentVersion", out var versionElement))
                        {
                            currentVersionString = versionElement.GetString() ?? "v0.0";
                            return;
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Failed to parse update_check.json: {ex.Message}");
                        // Fall through to version.txt check
                    }
                }

                // Fallback to version.txt
                string versionFilePath = Path.Combine(currentAppDirectory, "version.txt");
                if (File.Exists(versionFilePath))
                {
                    currentVersionString = File.ReadAllText(versionFilePath).Trim();
                }
                else
                {
                    currentVersionString = "v0.0";
                }
            }
            catch (Exception ex)
            {
                currentVersionString = "Unknown";
                Debug.WriteLine($"Failed to load version: {ex.Message}");
            }
        }

        protected override void OnOpened(EventArgs e)
        {
            base.OnOpened(e);
            ApplyRoundedCorners();
            _ = InitializeGamesAsync();
        }

        private async Task InitializeGamesAsync()
        {
            try
            {
                await _gameManager.LoadGamesAsync();

                ApplySorting();

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    DataContext = this;
                    UpdateContinueButtonState();
                    UpdateEmptyStateVisibility();

                    if (!_hasInitializedFocus)
                    {
                        SetInitialFocus();
                        _hasInitializedFocus = true;
                    }
                });
            }
            catch (Exception ex)
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                    _ = ShowMessageBoxAsync($"Failed to load apps: {ex.Message}", "Load Error"));
            }
        }

        private void SetInitialFocus()
        {
            // Small delay to ensure UI is fully rendered
            Dispatcher.UIThread.Post(() =>
            {
                // Try to focus the Continue button if visible
                if (IsContinueVisible && this.FindControl<Button>("ContinueButton") is Button continueBtn)
                {
                    continueBtn.Focus();
                    return;
                }

                // Try Settings button
                if (this.FindControl<Button>("SettingsButton") is Button settingsBtn)
                {
                    settingsBtn.Focus();
                    return;
                }

                // Fallback to first focusable control
                var firstFocusable = this.GetVisualDescendants()
                    .OfType<Control>()
                    .FirstOrDefault(c => c.IsVisible && c.IsEnabled && c.Focusable);

                firstFocusable?.Focus();
            }, DispatcherPriority.Loaded);
        }

        public void CloseLauncher_Click(object sender, RoutedEventArgs e)
        {
            // Close settings panel if open
            if (isSettingsPanelOpen && SettingsPanel != null)
            {
                isSettingsPanelOpen = false;
                SettingsPanel.IsVisible = false;
                return;
            }

            // Close changelog if open
            if (_isChangelogOpen)
            {
                CloseChangelog();
                return;
            }

            if (_isGamesManagerOpen)
            {
                CloseManageGames_Click(sender, e);
                return;
            }

            Close();
        }

        public void ToggleFullscreen_Click(object sender, RoutedEventArgs e)
        {
            IsFullscreen = !IsFullscreen;
            if (IsFullscreen)
            {
                WindowState = WindowState.FullScreen;
            }
            else
            {
                WindowState = WindowState.Normal;
            }
            OnPropertyChanged(nameof(WindowBackground));
        }

        public void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        public void LayoutPreset_Landscape_Click(object sender, RoutedEventArgs e)
        {
            _settings.IconFill = true;
            _settings.UseGridView = true;
            _settings.SlotSize = 304;
            _settings.IconSize = 220;
            _settings.IconMargin = 0;
            _settings.SlotTextMargin = 0;
            _settings.IconOpacity = 1.0f;
            InfoTextLength = "90";
            OnSettingChanged();
            UpdateSettingsUI();
        }

        public void LayoutPreset_Portrait_Click(object sender, RoutedEventArgs e)
        {
            _settings.IconFill = true;
            _settings.UseGridView = true;
            _settings.SlotSize = 144;
            _settings.IconSize = 200;
            _settings.IconMargin = 0;
            _settings.SlotTextMargin = 0;
            _settings.IconOpacity = 1.0f;
            InfoTextLength = "90";
            OnSettingChanged();
            UpdateSettingsUI();
        }

        public void LayoutPreset_Square_Click(object sender, RoutedEventArgs e)
        {
            _settings.IconFill = true;
            _settings.UseGridView = true;
            _settings.SlotSize = 220;
            _settings.IconSize = 220;
            _settings.IconMargin = 0;
            _settings.SlotTextMargin = 0;
            _settings.IconOpacity = 1.0f;
            InfoTextLength = "*";
            OnSettingChanged();
            UpdateSettingsUI();
        }

        public void LayoutPreset_Grid_Click(object sender, RoutedEventArgs e)
        {
            _settings.IconFill = true;
            _settings.UseGridView = true;
            _settings.SlotSize = 272;
            _settings.IconSize = 200;
            _settings.IconMargin = 0;
            _settings.SlotTextMargin = 0;
            _settings.IconOpacity = 1.0f;
            InfoTextLength = "*";
            OnSettingChanged();
            UpdateSettingsUI();
        }

        public void LayoutPreset_List_Click(object sender, RoutedEventArgs e)
        {
            _settings.IconFill = false;
            _settings.UseGridView = false;
            _settings.SlotSize = 120;
            _settings.IconSize = 116;
            _settings.IconMargin = 8;
            _settings.SlotTextMargin = 112;
            _settings.IconOpacity = 1.0f;
            InfoTextLength = "*";
            OnSettingChanged();
            UpdateSettingsUI();
        }

        private async void GameButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.DataContext is GameInfo game)
            {
                try
                {
                    if (game.Status == GameStatus.UpdateAvailable)
                    {
                        ShowUpdateActionMenu(button, game);
                        return;
                    }

                    await game.PerformActionAsync(_gameManager.HttpClient, _gameManager.GamesFolder, _settings);

                    // Check if multiple downloads need selection
                    if ((game.Status == GameStatus.NotInstalled || game.Status == GameStatus.UpdateAvailable) &&
                        game.HasMultipleDownloads && game.SelectedDownload == null)
                    {
                        if (button != null)
                        {
                            ShowDownloadSelectionMenu(button, game);
                        }
                        return;
                    }

                    // Check if multiple executables need selection
                    if (game.Status == GameStatus.Installed && game.HasMultipleExecutables && string.IsNullOrEmpty(game.SelectedExecutable))
                    {
                        if (button != null)
                        {
                            ShowExecutableSelectionMenu(button, game);
                        }
                        return;
                    }

                    UpdateContinueButtonState();
                }
                catch (Exception ex)
                {
                    await ShowMessageBoxAsync($"Failed to perform action for {game.Name}: {ex.Message}", "Action Error");
                }
                if (_settings.CloseAfterLaunch)
                {
                    Close();
                }
            }
        }

        private void ShowUpdateActionMenu(Control anchor, GameInfo game)
        {
            var contextMenu = new ContextMenu();

            contextMenu.Items.Add(new MenuItem
            {
                Header = $"Update options for {game.Name}:",
                IsEnabled = false,
                FontWeight = FontWeight.Bold
            });
            contextMenu.Items.Add(new Separator());

            var updateNowItem = new MenuItem { Header = "Update Now" };
            updateNowItem.Click += async (_, _) => await HandleUpdateNowAsync(anchor, game);
            contextMenu.Items.Add(updateNowItem);

            var skipItem = new MenuItem { Header = "Skip Update" };
            skipItem.Click += async (_, _) => await HandleSkipUpdateAsync(game);
            contextMenu.Items.Add(skipItem);

            var changeVersionItem = new MenuItem { Header = "Change Version" };
            changeVersionItem.Click += async (_, _) => await HandleChangeVersionAsync(anchor, game);
            contextMenu.Items.Add(changeVersionItem);

            contextMenu.Items.Add(new Separator());
            contextMenu.Items.Add(new MenuItem { Header = "Cancel" });

            OpenContextMenu(anchor, contextMenu);
        }

        private void ShowDownloadSelectionMenu(Button sourceButton, GameInfo game)
        {
            if (game.AvailableDownloads == null || game.AvailableDownloads.Count == 0)
                return;

            var contextMenu = new ContextMenu();

            // Add header
            var headerItem = new MenuItem
            {
                Header = "Select download file:",
                IsEnabled = false,
                FontWeight = FontWeight.Bold
            };
            contextMenu.Items.Add(headerItem);
            contextMenu.Items.Add(new Separator());

            // Get platform identifier for matching
            string platformIdentifier = GameInfo.GetPlatformIdentifier(_settings);

            // Sort downloads: preferred platform first, then others
            var sortedDownloads = game.AvailableDownloads
                .OrderByDescending(asset => GameInfo.MatchesPlatform(asset.name, platformIdentifier))
                .ToList();

            // Add download options
            foreach (var asset in sortedDownloads)
            {
                bool isPreferred = GameInfo.MatchesPlatform(asset.name, platformIdentifier);

                // Detect platform icon
                string? iconPath = GameInfo.GetPlatformIcon(asset.name);

                // Create grid
                var contentGrid = new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("Auto,*"),
                    HorizontalAlignment = HorizontalAlignment.Stretch
                };

                // Add icon if detected
                if (!string.IsNullOrEmpty(iconPath))
                {
                    var icon = new Avalonia.Controls.Image
                    {
                        Source = new Avalonia.Media.Imaging.Bitmap(
                            Avalonia.Platform.AssetLoader.Open(new Uri(iconPath))),
                        Width = 28,
                        Height = 28,
                        Margin = new Thickness(0, 0, 10, 0),
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    Grid.SetColumn(icon, 0);
                    contentGrid.Children.Add(icon);
                }

                // Add filename text
                var displayName = asset.name + (isPreferred ? " (Recommended)" : "");
                var textBlock = new TextBlock
                {
                    Text = displayName,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis
                };
                Grid.SetColumn(textBlock, 1);
                contentGrid.Children.Add(textBlock);

                var menuItem = new MenuItem
                {
                    Header = contentGrid,
                    Tag = asset
                };

                if (isPreferred)
                {
                    menuItem.Classes.Add("accent");
                }

                menuItem.Click += async (s, e) =>
                {
                    var selectedAsset = (s as MenuItem)?.Tag as GitHubAsset;
                    game.SelectedDownload = selectedAsset;
                    try
                    {
                        await game.PerformActionAsync(_gameManager.HttpClient, _gameManager.GamesFolder, _settings);
                    }
                    catch (Exception ex)
                    {
                        await ShowMessageBoxAsync($"Failed to download {game.Name}: {ex.Message}", "Download Error");
                    }
                };

                contextMenu.Items.Add(menuItem);
            }

            contextMenu.Items.Add(new Separator());

            // Add cancel option
            var cancelItem = new MenuItem
            {
                Header = "Cancel"
            };
            cancelItem.Click += (s, e) =>
            {
                game.SelectedDownload = null;
            };
            contextMenu.Items.Add(cancelItem);

            // Attach to button and open
            sourceButton.ContextMenu = contextMenu;
            contextMenu.PlacementTarget = sourceButton;
            contextMenu.Placement = PlacementMode.Bottom;

            // Focus first download item when opened
            contextMenu.Opened += (s, e) =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    var firstDownloadItem = contextMenu.Items.OfType<MenuItem>()
                        .Skip(1)
                        .FirstOrDefault(item => item is MenuItem mi && mi.IsEnabled);
                    firstDownloadItem?.Focus();
                }, DispatcherPriority.Loaded);
            };

            contextMenu.Open(sourceButton);
        }

        private Control? ResolveMenuAnchor(Control sourceControl)
        {
            if (sourceControl is MenuItem menuItem)
            {
                var parent = menuItem.Parent as Control;
                while (parent != null)
                {
                    if (parent is ContextMenu parentMenu && parentMenu.PlacementTarget is Control parentTarget)
                    {
                        return parentTarget;
                    }

                    parent = parent.Parent as Control;
                }

                var visualMenu = menuItem.GetVisualAncestors().OfType<ContextMenu>().FirstOrDefault();
                if (visualMenu?.PlacementTarget is Control visualTarget)
                {
                    return visualTarget;
                }
            }

            return sourceControl;
        }

        private Control? FindGameMenuAnchor(GameInfo game)
        {
            return this.GetVisualDescendants()
                .OfType<Button>()
                .FirstOrDefault(button =>
                    ReferenceEquals(button.DataContext, game) ||
                    ReferenceEquals(button.Tag, game));
        }

        private void OpenContextMenu(Control anchor, ContextMenu contextMenu)
        {
            if (double.IsNaN(contextMenu.MaxHeight) || contextMenu.MaxHeight <= 0)
            {
                var availableHeight = Bounds.Height > 0 ? Bounds.Height - 120 : 560;
                contextMenu.MaxHeight = Math.Max(240, availableHeight);
            }

            contextMenu.PlacementTarget = anchor;
            contextMenu.Placement = PlacementMode.Bottom;
            contextMenu.Open(anchor);
        }

        private void GameCard_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (!e.GetCurrentPoint(this).Properties.IsRightButtonPressed)
            {
                return;
            }

            if (sender is not Control card)
            {
                return;
            }

            var optionsButton = card.GetVisualDescendants()
                .OfType<Button>()
                .FirstOrDefault(button => button.ContextMenu != null);

            if (optionsButton?.ContextMenu == null)
            {
                return;
            }

            optionsButton.ContextMenu.PlacementTarget = optionsButton;
            optionsButton.ContextMenu.Placement = PlacementMode.Bottom;
            if (double.IsNaN(optionsButton.ContextMenu.MaxHeight) || optionsButton.ContextMenu.MaxHeight <= 0)
            {
                var availableHeight = Bounds.Height > 0 ? Bounds.Height - 120 : 560;
                optionsButton.ContextMenu.MaxHeight = Math.Max(240, availableHeight);
            }

            optionsButton.ContextMenu.Open(optionsButton);
            e.Handled = true;
        }

        private async Task HandleUpdateNowAsync(Control anchor, GameInfo game)
        {
            try
            {
                game.IsLoading = true;
                var releases = await game.FetchReleasesAsync(_gameManager.HttpClient);
                var latestRelease = releases.FirstOrDefault();
                if (latestRelease == null)
                {
                    await ShowMessageBoxAsync(game.NoReleaseMessage, "No Releases");
                    if (OperatingSystem.IsMacOS()) await ConfigureMacLaunchAsync(game);
                    return;
                }

                var availableAssets = game.GetDownloadableAssets(latestRelease);

                if (availableAssets.Count == 0)
                {
                    await ShowMessageBoxAsync($"No downloadable files were found for {game.Name}.", "No Assets");
                    return;
                }

                if (availableAssets.Count == 1)
                {
                    await game.InstallReleaseAsync(_gameManager.HttpClient, _gameManager.GamesFolder, _settings, latestRelease, availableAssets[0]);
                    await PersistGameVersionPreferencesAsync(game, null, latestRelease.tag_name);
                    ApplySorting();
                    UpdateContinueButtonState();
                    return;
                }

                ShowReleaseDownloadSelectionMenu(anchor, game, latestRelease, null, latestRelease.tag_name);
            }
            catch (Exception ex)
            {
                await ShowMessageBoxAsync($"Failed to update {game.Name}: {ex.Message}", "Update Error");
            }
            finally
            {
                game.IsLoading = false;
            }
        }

        private async Task HandleSkipUpdateAsync(GameInfo game)
        {
            game.SkipLatestUpdate();
            await PersistGameVersionPreferencesAsync(game, game.PreferredVersion, game.SkippedUpdateVersion);
            ApplySorting();
            UpdateContinueButtonState();
        }

        private async Task HandleChangeVersionAsync(Control anchor, GameInfo game)
        {
            try
            {
                game.IsLoading = true;
                var releases = await game.FetchReleasesAsync(_gameManager.HttpClient);
                if (releases.Count == 0)
                {
                    await ShowMessageBoxAsync(game.NoReleaseMessage, "No Releases");
                    if (OperatingSystem.IsMacOS()) await ConfigureMacLaunchAsync(game);
                    return;
                }

                ShowVersionSelectionMenu(anchor, game, releases);
            }
            catch (Exception ex)
            {
                await ShowMessageBoxAsync($"Failed to load versions for {game.Name}: {ex.Message}", "Version Selection Error");
            }
            finally
            {
                game.IsLoading = false;
            }
        }

        private void ShowVersionSelectionMenu(Control anchor, GameInfo game, IReadOnlyList<GitHubRelease> releases)
        {
            var contextMenu = new ContextMenu();
            contextMenu.Items.Add(new MenuItem
            {
                Header = $"Choose a version for {game.Name}:",
                IsEnabled = false,
                FontWeight = FontWeight.Bold
            });
            contextMenu.Items.Add(new Separator());

            foreach (var release in releases)
            {
                var tags = new List<string>();
                if (!string.IsNullOrWhiteSpace(game.LatestVersion) &&
                    release.tag_name.Equals(game.LatestVersion, StringComparison.OrdinalIgnoreCase))
                {
                    tags.Add("Latest");
                }
                if (!string.IsNullOrWhiteSpace(game.InstalledVersion) &&
                    release.tag_name.Equals(game.InstalledVersion, StringComparison.OrdinalIgnoreCase))
                {
                    tags.Add("Installed");
                }
                if (!string.IsNullOrWhiteSpace(game.PreferredVersion) &&
                    release.tag_name.Equals(game.PreferredVersion, StringComparison.OrdinalIgnoreCase))
                {
                    tags.Add("Preferred");
                }
                if (release.prerelease)
                {
                    tags.Add("Pre-release");
                }

                var header = release.tag_name;
                if (tags.Count > 0)
                {
                    header += $" ({string.Join(", ", tags)})";
                }

                var versionItem = new MenuItem { Header = header };
                versionItem.Click += (_, _) =>
                {
                    var acknowledgedVersion = string.IsNullOrWhiteSpace(game.LatestVersion)
                        ? release.tag_name
                        : game.LatestVersion;
                    ShowReleaseDownloadSelectionMenu(anchor, game, release, release.tag_name, acknowledgedVersion);
                };

                if (!string.IsNullOrWhiteSpace(game.LatestVersion) &&
                    release.tag_name.Equals(game.LatestVersion, StringComparison.OrdinalIgnoreCase))
                {
                    versionItem.Classes.Add("accent");
                }

                contextMenu.Items.Add(versionItem);
            }

            contextMenu.Items.Add(new Separator());
            contextMenu.Items.Add(new MenuItem { Header = "Cancel" });
            OpenContextMenu(anchor, contextMenu);
        }

        private void ShowReleaseDownloadSelectionMenu(Control anchor, GameInfo game, GitHubRelease release, string preferredVersion, string skippedUpdateVersion)
        {
            var availableAssets = game.GetDownloadableAssets(release)
                .OrderByDescending(asset => GameInfo.MatchesPlatform(asset.name, GameInfo.GetPlatformIdentifier(_settings)))
                .ToList();

            if (availableAssets.Count == 0)
                return;

            var contextMenu = new ContextMenu();
            contextMenu.Items.Add(new MenuItem
            {
                Header = $"Choose a download for {release.tag_name}:",
                IsEnabled = false,
                FontWeight = FontWeight.Bold
            });
            contextMenu.Items.Add(new Separator());

            string platformIdentifier = GameInfo.GetPlatformIdentifier(_settings);

            foreach (var asset in availableAssets)
            {
                bool isPreferred = GameInfo.MatchesPlatform(asset.name, platformIdentifier);
                string? iconPath = GameInfo.GetPlatformIcon(asset.name);

                var contentGrid = new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("Auto,*"),
                    HorizontalAlignment = HorizontalAlignment.Stretch
                };

                if (!string.IsNullOrEmpty(iconPath))
                {
                    var icon = new Avalonia.Controls.Image
                    {
                        Source = new Avalonia.Media.Imaging.Bitmap(Avalonia.Platform.AssetLoader.Open(new Uri(iconPath))),
                        Width = 28,
                        Height = 28,
                        Margin = new Thickness(0, 0, 10, 0),
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    Grid.SetColumn(icon, 0);
                    contentGrid.Children.Add(icon);
                }

                var displayName = asset.name + (isPreferred ? " (Recommended)" : "");
                var textBlock = new TextBlock
                {
                    Text = displayName,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis
                };
                Grid.SetColumn(textBlock, 1);
                contentGrid.Children.Add(textBlock);

                var menuItem = new MenuItem
                {
                    Header = contentGrid
                };

                if (isPreferred)
                {
                    menuItem.Classes.Add("accent");
                }

                menuItem.Click += async (_, _) =>
                {
                    try
                    {
                        await game.InstallReleaseAsync(_gameManager.HttpClient, _gameManager.GamesFolder, _settings, release, asset);
                        if (!string.IsNullOrWhiteSpace(skippedUpdateVersion))
                        {
                            game.LatestVersion = skippedUpdateVersion;
                        }
                        await PersistGameVersionPreferencesAsync(game, preferredVersion, skippedUpdateVersion);
                        ApplySorting();
                        UpdateContinueButtonState();
                    }
                    catch (Exception ex)
                    {
                        await ShowMessageBoxAsync($"Failed to download {game.Name}: {ex.Message}", "Download Error");
                    }
                };

                contextMenu.Items.Add(menuItem);
            }

            contextMenu.Items.Add(new Separator());
            contextMenu.Items.Add(new MenuItem { Header = "Cancel" });
            OpenContextMenu(anchor, contextMenu);
        }

        private void ShowExecutableSelectionMenu(Control anchor, GameInfo game)
        {
            if (game.AvailableExecutables == null || game.AvailableExecutables.Count == 0)
                return;

            var contextMenu = new ContextMenu();

            // Add header
            var headerItem = new MenuItem
            {
                Header = "Select executable to launch:",
                IsEnabled = false,
                FontWeight = FontWeight.Bold
            };
            contextMenu.Items.Add(headerItem);
            contextMenu.Items.Add(new Separator());

            // Add executable options
            foreach (var exe in game.AvailableExecutables)
            {
                var displayName = Path.GetFileName(exe);
                var menuItem = new MenuItem
                {
                    Header = displayName,
                    Tag = exe
                };

                menuItem.Click += async (s, e) =>
                {
                    var selectedExe = (s as MenuItem)?.Tag as string;
                    game.SelectedExecutable = selectedExe;

                    // Save the selection
                    if (!string.IsNullOrEmpty(selectedExe))
                    {
                        game.SaveSelectedExecutable(selectedExe, _gameManager.GamesFolder);
                    }

                    try
                    {
                        await game.PerformActionAsync(_gameManager.HttpClient, _gameManager.GamesFolder, _settings);
                    }
                    catch (Exception ex)
                    {
                        await ShowMessageBoxAsync($"Failed to launch {game.Name}: {ex.Message}", "Launch Error");
                    }
                };

                contextMenu.Items.Add(menuItem);
            }

            contextMenu.Items.Add(new Separator());

            // Add cancel option
            var cancelItem = new MenuItem
            {
                Header = "Cancel"
            };
            cancelItem.Click += (s, e) =>
            {
                game.SelectedExecutable = null;
            };
            contextMenu.Items.Add(cancelItem);

            // Focus first executable item when opened
            contextMenu.Opened += (s, e) =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    var firstExecutableItem = contextMenu.Items.OfType<MenuItem>()
                        .Skip(1)
                        .FirstOrDefault(item => item is MenuItem mi && mi.IsEnabled);
                    firstExecutableItem?.Focus();
                }, DispatcherPriority.Loaded);
            };

            OpenContextMenu(anchor, contextMenu);
        }

        private async void SelectDifferentExecutable_Click(object sender, RoutedEventArgs e)
        {
            var menuItem = sender as MenuItem;
            var game = menuItem?.CommandParameter as GameInfo;

            if (game == null)
            {
                _ = ShowMessageBoxAsync("Unable to identify the selected app.", "Error");
                return;
            }

            var anchor = (menuItem != null ? ResolveMenuAnchor(menuItem) : null) ?? FindGameMenuAnchor(game);
            if (anchor == null)
            {
                _ = ShowMessageBoxAsync("Unable to open the executable menu for this game.", "Error");
                return;
            }

            game.ClearSelectedExecutable(_gameManager.GamesFolder);
            game.AvailableExecutables = null;

            try
            {
                if (string.IsNullOrWhiteSpace(game.FolderName))
                {
                    await ShowMessageBoxAsync("This game is missing its install folder information.", "Executable Error");
                    return;
                }

                var gamePath = game.GetInstallPath(_gameManager.GamesFolder);
                if (!Directory.Exists(gamePath))
                {
                    await ShowMessageBoxAsync($"Could not find the install folder for {game.Name}.", "Executable Error");
                    return;
                }

                var executables = GameInfo.GetExecutableCandidates(gamePath, SearchOption.TopDirectoryOnly, out _);
                if (executables.Count == 0)
                {
                    executables = GameInfo.GetExecutableCandidates(gamePath, SearchOption.AllDirectories, out _);
                }

                if (executables.Count == 0)
                {
                    await ShowMessageBoxAsync($"No executable files were found for {game.Name}.", "Executable Not Found");
                    return;
                }

                game.AvailableExecutables = executables;

                if (executables.Count == 1)
                {
                    await ShowMessageBoxAsync($"Only one executable was found for {game.Name}, so there is nothing else to choose.", "Single Executable");
                    return;
                }

                ShowExecutableSelectionMenu(anchor, game);
            }
            catch (Exception ex)
            {
                await ShowMessageBoxAsync($"Failed to load executables for {game.Name}: {ex.Message}", "Executable Error");
            }
        }

        private void OptionsButton_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            if (button?.ContextMenu != null)
            {
                button.ContextMenu.PlacementTarget = button;
                button.ContextMenu.Placement = PlacementMode.Bottom;
                button.ContextMenu.Open();
            }
        }

        private async void ContinueButton_Click(object sender, RoutedEventArgs e)
        {
            var latestGame = _gameManager.GetLatestPlayedInstalledGame();
            if (latestGame != null)
            {
                try
                {
                    await latestGame.PerformActionAsync(_gameManager.HttpClient, _gameManager.GamesFolder, _settings);
                }
                catch (Exception ex)
                {
                    await ShowMessageBoxAsync($"Failed to launch {latestGame.Name}: {ex.Message}", "Launch Error");
                }
            }
            else
            {
                await ShowMessageBoxAsync("No installed apps found to continue.", "No App Found");
            }
        }

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            isSettingsPanelOpen = !isSettingsPanelOpen;
            SettingsPanel.IsVisible = isSettingsPanelOpen;

            if (isSettingsPanelOpen)
            {
                Dispatcher.UIThread.Post(() =>
                {
                    var firstSettingsControl = SettingsContent?.GetVisualDescendants()
                        .OfType<Control>()
                        .FirstOrDefault(c => c.IsVisible && c.IsEnabled && c.Focusable);
                    firstSettingsControl?.Focus();
                }, DispatcherPriority.Loaded);
            }
        }

        private void UpdateSettingsUI()
        {
            if (_settings != null)
            {
                if (IconOpacitySlider != null)
                    IconOpacitySlider.Value = _settings.IconOpacity;

                if (IconSizeSlider != null)
                    IconSizeSlider.Value = _settings.IconSize;

                if (IconFillCheckBox != null)
                    IconFillCheckBox.IsChecked = _settings.IconFill;

                if (TextMarginSlider != null)
                    TextMarginSlider.Value = _settings.SlotTextMargin;

                if (IconMarginSlider != null)
                    IconMarginSlider.Value = _settings.IconMargin;

                if (SlotSizeSlider != null)
                    SlotSizeSlider.Value = _settings.SlotSize;

                if (UseGridViewCheckBox != null)
                    UseGridViewCheckBox.IsChecked = _settings.UseGridView;

                if (BackgroundOpacitySlider != null)
                    BackgroundOpacitySlider.Value = _settings.BackgroundOpacity;

                if (ShowOSTopBarCheckBox != null)
                    ShowOSTopBarCheckBox.IsChecked = _settings.ShowOSTopBar;

                if (SortByComboBox != null)
                {
                    var savedSort = _settings.SortBy ?? "Name";
                    _currentSortBy = savedSort;

                    foreach (var entry in SortByComboBox.Items)
                    {
                        if (entry is ComboBoxItem item && item.Tag as string == savedSort)
                        {
                            SortByComboBox.SelectedItem = item;
                            break;
                        }
                    }
                }

                if (GitHubTokenTextBox != null)
                    GitHubTokenTextBox.Text = _settings.GitHubApiToken;

                if (GamePathTextBox != null)
                    GamePathTextBox.Text = _settings.AppsPath;

                if (LinuxWindowsLaunchCommandTextBox != null)
                    LinuxWindowsLaunchCommandTextBox.Text = _settings.LinuxWindowsLaunchCommand;

                if (AppListRepositoryTextBox != null)
                    AppListRepositoryTextBox.Text = _settings.AppListRepository;

                if (EnableCommunityCatalogCheckBox != null)
                    EnableCommunityCatalogCheckBox.IsChecked = _settings.EnableCommunityCatalog;

                if (StartFullscreenCheckBox != null)
                    StartFullscreenCheckBox.IsChecked = _settings.StartFullscreen;

                if (RoundWindowCornersCheckBox != null)
                    RoundWindowCornersCheckBox.IsChecked = _settings.WindowBorderRounding;

                if (EnableGamepadCheckBox != null)
                    EnableGamepadCheckBox.IsChecked = _settings.EnableGamepadInput;

                if (CloseAfterLaunchCheckBox != null)
                    CloseAfterLaunchCheckBox.IsChecked = _settings.CloseAfterLaunch;

                PlatformString = _settings.Platform switch
                {
                    TargetOS.Auto => "Automatic",
                    TargetOS.Windows => "Windows",
                    TargetOS.MacOS => "macOS",
                    TargetOS.LinuxX64 => "Linux x64",
                    TargetOS.LinuxARM64 => "Linux ARM64",
                    _ => "Unknown"
                };

                // Initialize theme
                ThemeColorBrush = new SolidColorBrush(Color.Parse(_settings?.PrimaryColor ?? "#18181b"));
                SecondaryColorBrush = new SolidColorBrush(Color.Parse(_settings?.SecondaryColor ?? "#404040"));
                UpdateThemeColors();
            }
        }

        private void OnSettingChanged()
        {
            try
            {
                AppSettings.Save(_settings);
            }
            catch (Exception ex)
            {
                _ = ShowMessageBoxAsync($"Failed to save settings: {ex.Message}", "Save Error");
            }
        }

        private void StartFullscreenCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            if (_settings != null)
            {
                _settings.StartFullscreen = true;
                OnSettingChanged();
            }
        }

        private void StartFullscreenCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            if (_settings != null)
            {
                _settings.StartFullscreen = false;
                OnSettingChanged();
            }
        }

        private void RoundWindowCornersCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            if (_settings != null)
            {
                _settings.WindowBorderRounding = true;
                ApplyRoundedCorners();
                OnPropertyChanged(nameof(WindowBackground));
                OnSettingChanged();
            }
        }

        private void RoundWindowCornersCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            if (_settings != null)
            {
                _settings.WindowBorderRounding = false;
                ApplyRoundedCorners();
                OnPropertyChanged(nameof(WindowBackground));
                OnSettingChanged();
            }
        }

        private void ShowOSTopBarCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            if (_settings != null)
            {
                _settings.ShowOSTopBar = true;
                OnPropertyChanged(nameof(ExtendClientAreaEnabled));
                OnPropertyChanged(nameof(ChromeHints));
                OnPropertyChanged(nameof(WindowBackground));
                OnSettingChanged();
            }
        }

        private void ShowOSTopBarCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            if (_settings != null)
            {
                _settings.ShowOSTopBar = false;
                OnPropertyChanged(nameof(ExtendClientAreaEnabled));
                OnPropertyChanged(nameof(ChromeHints));
                OnPropertyChanged(nameof(WindowBackground));
                OnSettingChanged();
            }
        }

        private void UseGridViewCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            _settings.UseGridView = true;
            OnSettingChanged();
        }

        private void UseGridViewCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            _settings.UseGridView = false;
            OnSettingChanged();
        }

        private void SlotSizeSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (_settings != null)
            {
                _settings.SlotSize = (int)e.NewValue;
                OnSettingChanged();
            }
        }

        private void IconOpacitySlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (_settings != null)
            {
                _settings.IconOpacity = (float)e.NewValue;
                OnSettingChanged();
            }
        }

        private void IconSizeSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (_settings != null)
            {
                _settings.IconSize = (int)e.NewValue;
                OnSettingChanged();
            }
        }

        private void IconMarginSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (_settings != null)
            {
                _settings.IconMargin = (int)e.NewValue;
                OnSettingChanged();
            }
        }

        private void TextMarginSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (_settings != null)
            {
                _settings.SlotTextMargin = (int)e.NewValue;
                OnSettingChanged();
            }
        }

        private void IconFillCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            if (_settings != null)
            {
                _settings.IconFill = true;
                OnSettingChanged();
            }
        }

        private void IconFillCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            if (_settings != null)
            {
                _settings.IconFill = false;
                OnSettingChanged();
            }
        }

        private void PlatformAuto_Click(object sender, RoutedEventArgs e)
        {             
            if (_settings != null)
            {
                _settings.Platform = TargetOS.Auto;
                PlatformString = "Automatic";
                OnSettingChanged();
            }
        }

        private void PlatformWindows_Click(object sender, RoutedEventArgs e)
        {
            if (_settings != null)
            {
                _settings.Platform = TargetOS.Windows;
                PlatformString = "Windows";
                OnSettingChanged();
            }
        }

        private void PlatformMacOS_Click(object sender, RoutedEventArgs e)
        {
            if (_settings != null)
            {
                _settings.Platform = TargetOS.MacOS;
                PlatformString = "macOS";
                OnSettingChanged();
            }
        }

        private void PlatformLinuxX64_Click(object sender, RoutedEventArgs e)
        {
            if (_settings != null)
            {
                _settings.Platform = TargetOS.LinuxX64;
                PlatformString = "Linux x64";
                OnSettingChanged();
            }
        }

        private void PlatformLinuxARM64_Click(object sender, RoutedEventArgs e)
        {
            if (_settings != null)
            {
                _settings.Platform = TargetOS.LinuxARM64;
                PlatformString = "Linux ARM64";
                OnSettingChanged();
            }
        }

        private DateTime? _lastUpdateTime = null;

        private async void CheckforUpdates_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var button = sender as Button;
                bool wasEnabled = button?.IsEnabled ?? true;
                string originalContent = string.Empty;

                if (button != null)
                {
                    // Store original content
                    if (button.Content is StackPanel panel)
                    {
                        originalContent = "original_stackpanel";
                    }

                    button.IsEnabled = false;

                    // Create a temporary text block for status
                    var statusText = new TextBlock
                    {
                        Text = "Checking launcher...",
                        VerticalAlignment = VerticalAlignment.Center,
                        HorizontalAlignment = HorizontalAlignment.Center
                    };
                    button.Content = statusText;
                }

                // Check for app updates
                if (_app != null)
                {
                    await _app.CheckForAppUpdatesManually();
                }

                // Update status text
                if (button?.Content is TextBlock textBlock)
                {
                    textBlock.Text = "Checking games...";
                }

                // Check game updates
                await _gameManager.CheckAllUpdatesAsync();
                ApplySorting();

                // Restore original button state
                _lastUpdateTime = DateTime.Now;
                if (button != null)
                {
                    button.IsEnabled = true;

                    // Restore original content
                    button.Content = new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Children =
                {
                    new Image
                    {
                        Width = 32,
                        Height = 32,
                        Source = new Avalonia.Media.Imaging.Bitmap(
                            Avalonia.Platform.AssetLoader.Open(
                                new Uri("avares://GithubLauncher/Assets/CheckForUpdates.png"))),
                        Margin = new Thickness(0, 0, 12, 0),
                        VerticalAlignment = VerticalAlignment.Center
                    },
                    new StackPanel
                    {
                        Orientation = Orientation.Vertical,
                        VerticalAlignment = VerticalAlignment.Center,
                        Children =
                        {
                            new TextBlock
                            {
                                Text = "Up to Date",
                                FontSize = 12,
                                FontWeight = FontWeight.Bold
                            },
                            new TextBlock
                            {
                                Text = GetLastCheckedText(),
                                FontSize = 11,
                                Foreground = new SolidColorBrush(Color.Parse("#B8B8B8"))
                            }
                        }
                    }
                }
                    };
                }
            }
            catch (Exception ex)
            {
                var button = sender as Button;
                if (button != null)
                {
                    button.IsEnabled = true;

                    // Restore original content on error
                    button.Content = new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Children =
                {
                    new Image
                    {
                        Width = 32,
                        Height = 32,
                        Source = new Avalonia.Media.Imaging.Bitmap(
                            Avalonia.Platform.AssetLoader.Open(
                                new Uri("avares://GithubLauncher/Assets/CheckForUpdates.png"))),
                        Margin = new Thickness(0, 0, 12, 0)
                    },
                    new TextBlock
                    {
                        Text = "Check for Updates",
                        VerticalAlignment = VerticalAlignment.Center
                    }
                }
                    };
                }
                await ShowMessageBoxAsync($"Failed to check for updates: {ex.Message}", "Error");
            }
        }

        private string GetLastCheckedText()
        {
            if (_lastUpdateTime == null)
            {
                return "Check for Updates";
            }

            var timeSince = DateTime.Now - _lastUpdateTime.Value;
            
            if (timeSince.TotalMinutes < 1)
            {
                return "Last Checked Just Now";
            }
            else if (timeSince.TotalMinutes < 2)
            {
                return "Last Checked 1 Minute Ago";
            }
            else if (timeSince.TotalMinutes < 60)
            {
                return $"Last Checked {timeSince.Minutes} Minutes Ago";
            }
            else if (timeSince.TotalHours < 2)
            {
                return "Last Checked 1 Hour Ago";
            }
            else if (timeSince.TotalHours < 24)
            {
                return $"Last Checked {timeSince.Hours} Hours Ago";
            }
            else if (timeSince.TotalDays < 2)
            {
                return "Last Checked 1 Day Ago";
            }
            else
            {
                return $"Last Checked {timeSince.Days} Days Ago";
            }
        }

        private void OpenFolder_Click(object sender, RoutedEventArgs e)
        {
            var menuItem = sender as MenuItem;
            var game = menuItem?.CommandParameter as GameInfo;
            if (game != null && !string.IsNullOrEmpty(game.FolderName))
            {
                try
                {
                    string folderPath = game.GetInstallPath(_gameManager.GamesFolder);
                    OpenUrl(folderPath);
                }
                catch (Exception ex)
                {
                    _ = ShowMessageBoxAsync($"Failed to open folder: {ex.Message}", "Action Error");
                }
            }
            else
            {
                _ = ShowMessageBoxAsync("Unable to identify the game folder.", "Action Error");
            }
        }

        private async void ForceUpdate_Click(object sender, RoutedEventArgs e)
        {
            var menuItem = sender as MenuItem;
            var game = menuItem?.CommandParameter as GameInfo;

            if (game == null)
            {
                await ShowMessageBoxAsync("Unable to identify the selected app.", "Error");
                return;
            }

            try
            {
                await PersistGameVersionPreferencesAsync(game, null, null);
                await game.ForceUpdateAsync(_gameManager.HttpClient, _gameManager.GamesFolder);
                ApplySorting();
            }
            catch (Exception ex)
            {
                await ShowMessageBoxAsync($"Failed to force update {game.Name}: {ex.Message}", "Force Update Failed");
            }
        }

        private void KofiButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string url = "https://ko-fi.com/sirdiabo/";
                OpenUrl(url);
            }
            catch (Exception ex)
            {
                _ = ShowMessageBoxAsync($"Failed to open Ko-Fi link: {ex.Message}", "Action Error");
            }
        }

        private void GithubButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string url = "https://github.com/GitOpsGarage/GithubLauncher-macOS/";
                OpenUrl(url);
            }
            catch (Exception ex)
            {
                _ = ShowMessageBoxAsync($"Failed to open Github link: {ex.Message}", "Action Error");
            }
        }

        private async void LaunchGameMenu_Click(object sender, RoutedEventArgs e)
        {
            var menuItem = sender as MenuItem;
            var game = menuItem?.CommandParameter as GameInfo;

            if (game == null)
            {
                await ShowMessageBoxAsync("Unable to identify the selected app.", "Error");
                return;
            }

            try
            {
                await game.PerformActionAsync(_gameManager.HttpClient, _gameManager.GamesFolder, _settings);
            }
            catch (Exception ex)
            {
                await ShowMessageBoxAsync($"Failed to launch {game.Name}: {ex.Message}", "Launch Error");
            }
        }

        private async void LocateExistingInstall_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuItem menuItem || menuItem.CommandParameter is not GameInfo game)
            {
                await ShowMessageBoxAsync("Unable to identify the selected app.", "Error");
                return;
            }

            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = $"Select existing install folder for {game.Name}",
                AllowMultiple = false
            });

            if (folders == null || folders.Count == 0)
                return;

            var selectedPath = folders[0].Path.LocalPath;
            if (string.IsNullOrWhiteSpace(selectedPath) || !Directory.Exists(selectedPath))
            {
                await ShowMessageBoxAsync("The selected install folder could not be found.", "Install Folder Not Found");
                return;
            }

            var folderName = Path.GetFileName(selectedPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (string.IsNullOrWhiteSpace(folderName))
            {
                await ShowMessageBoxAsync("The selected folder does not have a valid folder name.", "Invalid Folder");
                return;
            }

            game.InstallPath = selectedPath;
            game.FolderName = folderName;

            await PersistGameInstallLocationAsync(game);
            await game.CheckStatusAsync(_gameManager.HttpClient, _gameManager.GamesFolder, forceUpdateCheck: true);

            ApplySorting();
            UpdateContinueButtonState();
        }

        private async void UpdateNowMenu_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuItem menuItem || menuItem.CommandParameter is not GameInfo game)
            {
                await ShowMessageBoxAsync("Unable to identify the selected app.", "Error");
                return;
            }

            var anchor = ResolveMenuAnchor(menuItem) ?? FindGameMenuAnchor(game);
            if (anchor == null)
            {
                await ShowMessageBoxAsync("Unable to open the update menu for this game.", "Error");
                return;
            }

            await HandleUpdateNowAsync(anchor, game);
        }

        private async void SkipUpdate_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuItem menuItem || menuItem.CommandParameter is not GameInfo game)
            {
                await ShowMessageBoxAsync("Unable to identify the selected app.", "Error");
                return;
            }

            await HandleSkipUpdateAsync(game);
        }

        private async void ChangeVersion_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuItem menuItem || menuItem.CommandParameter is not GameInfo game)
            {
                await ShowMessageBoxAsync("Unable to identify the selected app.", "Error");
                return;
            }

            var anchor = ResolveMenuAnchor(menuItem) ?? FindGameMenuAnchor(game);
            if (anchor == null)
            {
                await ShowMessageBoxAsync("Unable to open the version menu for this game.", "Error");
                return;
            }

            await HandleChangeVersionAsync(anchor, game);
        }

        private void OpenGitHubPage_Click(object sender, RoutedEventArgs e)
        {
            var menuItem = sender as MenuItem;
            var game = menuItem?.CommandParameter as GameInfo;

            if (game != null && !string.IsNullOrEmpty(game.Repository))
            {
                try
                {
                    var githubUrl = $"https://github.com/{game.Repository}";
                    OpenUrl(githubUrl);
                }
                catch (Exception ex)
                {
                    _ = ShowMessageBoxAsync($"Failed to open GitHub page: {ex.Message}", "Error");
                }
            }
            else _ = ShowMessageBoxAsync($"Failed to open GitHub page", "Error");
        }

        private async void SetCustomIcon_Click(object sender, RoutedEventArgs e)
        {
            var menuItem = sender as MenuItem;
            var selectedGame = menuItem?.CommandParameter as GameInfo;
            if (selectedGame == null)
            {
                _ = ShowMessageBoxAsync("Unable to identify the selected app.", "Error");
                return;
            }

            bool hasExistingCustomIcon = !string.IsNullOrEmpty(selectedGame.CustomIconPath);
            string confirmMessage = hasExistingCustomIcon
                ? $"Replace the existing custom icon for {selectedGame.Name}?"
                : $"Set custom icon for {selectedGame.Name}?";

            if (hasExistingCustomIcon)
            {
                var confirmResult = await ShowMessageBoxAsync(confirmMessage, "Confirm Icon Replacement", true);
                if (!confirmResult)
                    return;
            }

            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = $"Select Custom Icon for {selectedGame.Name}",
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("Image Files")
                    {
                        Patterns = new[] { "*.png", "*.jpg", "*.jpeg", "*.webp", "*.bmp", "*.gif", "*.ico" }
                    },
                    new FilePickerFileType("PNG Files") { Patterns = new[] { "*.png" } },
                    new FilePickerFileType("JPEG Files") { Patterns = new[] { "*.jpg", "*.jpeg" } },
                    new FilePickerFileType("WebP Files") { Patterns = new[] { "*.webp" } },
                    new FilePickerFileType("Bitmap Files") { Patterns = new[] { "*.bmp" } },
                    new FilePickerFileType("Icon Files") { Patterns = new[] { "*.ico" } },
                    new FilePickerFileType("All Files") { Patterns = new[] { "*" } }
                },
                AllowMultiple = false
            });

            if (files?.Count > 0)
            {
                try
                {
                    var filePath = files[0].Path.LocalPath;
                    selectedGame.SetCustomIcon(filePath, _gameManager.CacheFolder);
                }
                catch (Exception ex)
                {
                    string errorMessage = hasExistingCustomIcon
                        ? $"Failed to replace custom icon: {ex.Message}"
                        : $"Failed to set custom icon: {ex.Message}";
                    _ = ShowMessageBoxAsync(errorMessage, "Error");
                }
            }
        }

        private async void RemoveCustomIcon_Click(object sender, RoutedEventArgs e)
        {
            var menuItem = sender as MenuItem;
            var selectedGame = menuItem?.CommandParameter as GameInfo;
            if (selectedGame == null)
            {
                _ = ShowMessageBoxAsync("Unable to identify the selected app.", "Error");
                return;
            }

            if (string.IsNullOrEmpty(selectedGame.CustomIconPath))
            {
                _ = ShowMessageBoxAsync($"{selectedGame.Name} is already using the default icon.", "No Custom Icon");
                return;
            }

            var result = await ShowMessageBoxAsync($"Remove custom icon for {selectedGame.Name}?", "Confirm Removal", true);
            if (result)
            {
                try
                {
                    selectedGame.RemoveCustomIcon();
                }
                catch (Exception ex)
                {
                    _ = ShowMessageBoxAsync($"Failed to remove custom icon: {ex.Message}", "Error");
                }
            }
        }

        private async void DeleteGameFromLibrary_Click(object sender, RoutedEventArgs e)
        {
            var menuItem = sender as MenuItem;
            var game = menuItem?.CommandParameter as GameInfo;

            if (game == null) return;

            if (game.Status == GameStatus.NotInstalled)
            {
                _ = ShowMessageBoxAsync($"{game.Name} is not installed.", "Nothing to Delete");
                return;
            }

            var result = await ShowMessageBoxAsync(
                $"Are you sure you want to delete {game.Name}?\n\nThis will permanently remove all game files and cannot be undone.",
                "Confirm Deletion",
                true);

            if (result)
            {
                try
                {
                    if (string.IsNullOrEmpty(game.FolderName))
                    {
                        await ShowMessageBoxAsync($"Failed to delete {game.Name}: game folder is not configured.", "Deletion Failed");
                        return;
                    }

                    var gamePath = game.GetInstallPath(_gameManager.GamesFolder);

                    if (Directory.Exists(gamePath))
                    {
                        game.Status = GameStatus.Installing;
                        game.IsLoading = true;

                        await Task.Run(() => Directory.Delete(gamePath, true));

                        game.IsLoading = false;
                        await game.CheckStatusAsync(_gameManager.HttpClient, _gameManager.GamesFolder);

                        UpdateContinueButtonState();
                    }
                    else
                    {
                        await game.CheckStatusAsync(_gameManager.HttpClient, _gameManager.GamesFolder);
                    }
                }
                catch (Exception ex)
                {
                    game.IsLoading = false;
                    _ = ShowMessageBoxAsync($"Failed to delete {game.Name}: {ex.Message}", "Deletion Failed");
                }
            }
        }

        private void OpenUrl(string url)
        {
            try
            {
                if (OperatingSystem.IsWindows())
                {
                    Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                }
                else if (OperatingSystem.IsLinux())
                {
                    Process.Start("xdg-open", $"\"{url}\"");
                }
                else if (OperatingSystem.IsMacOS())
                {
                    Process.Start("open", $"\"{url}\"");
                }
            }
            catch (Exception ex)
            {
                _ = ShowMessageBoxAsync($"Failed to open URL: {ex.Message}", "Error");
            }
        }

        private async Task ShowMessageBoxAsync(string message, string title)
        {
            await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop &&
                    desktop.MainWindow != null)
                {
                    var messageBox = new Window
                    {
                        Title = title,
                        Width = 400,
                        Height = 150,
                        WindowStartupLocation = WindowStartupLocation.CenterOwner,
                        Content = new StackPanel
                        {
                            Margin = new Thickness(20),
                            Children =
                    {
                        new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 20) },
                        new Button { Content = "OK", HorizontalAlignment = HorizontalAlignment.Center }
                    }
                        }
                    };
                    if (((StackPanel)messageBox.Content).Children[1] is Button okButton)
                    {
                        okButton.Click += (s, e) => messageBox.Close();
                    }
                    await messageBox.ShowDialog(desktop.MainWindow);
                }
            });
        }

        private async void UnhideAllGamesButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _gameManager.UnhideAllGames();
                await _gameManager.LoadGamesAsync();
                ApplySorting();
            }
            catch (Exception ex)
            {
                _ = ShowMessageBoxAsync($"Failed to unhide games: {ex.Message}", "Error");
            }
        }

        private async void UnhideAllManuallyHidden_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.Tag is not string category)
                return;

            try
            {
                var settings = AppSettings.Load();
                var games = await LoadGamesFromJsonAsync();

                var targets = category switch
                {
                    "Stable" => games.Where(g => !g.IsExperimental && !g.IsCustom),
                    "Experimental" => games.Where(g => g.IsExperimental && !g.IsCustom),
                    "Custom" => games.Where(g => g.IsCustom),
                    _ => Enumerable.Empty<GameInfo>()
                };

                foreach (var game in targets)
                {
                    var key = !string.IsNullOrWhiteSpace(game.FolderName)
                        ? $"folder:{game.FolderName}"
                        : !string.IsNullOrWhiteSpace(game.Repository)
                            ? $"repo:{game.Repository}"
                            : $"name:{game.Name ?? string.Empty}";

                    settings.ManuallyHiddenApps.Remove(key);
                    if (!string.IsNullOrWhiteSpace(game.Name))
                        settings.ManuallyHiddenApps.Remove(game.Name);
                }

                AppSettings.Save(settings);
                await _gameManager.LoadGamesAsync();
                ApplySorting();
                LoadGamesFromJson();
            }
            catch (Exception ex)
            {
                _ = ShowMessageBoxAsync($"Failed to unhide games: {ex.Message}", "Error");
            }
        }

        private async void HideNonInstalledButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await _gameManager.HideAllNonInstalledGames();
                ApplySorting();
            }
            catch (Exception ex)
            {
                _ = ShowMessageBoxAsync($"Failed to hide non-installed apps: {ex.Message}", "Error");
            }
        }
        private void SortByComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.AddedItems.Count == 0) return;

            if (SortByComboBox?.SelectedItem is ComboBoxItem item && item.Tag is string sortMode)
            {
                _currentSortBy = sortMode;
                _settings.SortBy = sortMode;
                OnSettingChanged();
                ApplySorting();
            }
        }

        private void ApplySorting()
        {
            if (_gameManager?.Games == null || _gameManager.Games.Count == 0)
            {
                Debug.WriteLine("ApplySorting: No apps to sort");
                return;
            }

            Debug.WriteLine($"ApplySorting: Sorting {_gameManager.Games.Count} apps by {_currentSortBy}");

            List<GameInfo> sortedGames;

            switch (_currentSortBy)
            {
                case "Name":
                    sortedGames = _gameManager.Games.OrderBy(g => g.Name ?? string.Empty).ToList();
                    break;

                case "NameDesc":
                    sortedGames = _gameManager.Games.OrderByDescending(g => g.Name ?? string.Empty).ToList();
                    break;

                case "Installed":
                    sortedGames = _gameManager.Games
                        .OrderByDescending(g => g.IsInstalled)
                        .ThenBy(g => g.Name ?? string.Empty)
                        .ToList();
                    break;

                case "NotInstalled":
                    sortedGames = _gameManager.Games
                        .OrderBy(g => g.IsInstalled)
                        .ThenBy(g => g.Name ?? string.Empty)
                        .ToList();
                    break;

                case "LastPlayed":
                    sortedGames = _gameManager.Games
                        .OrderByDescending(g => GetLastPlayedTime(g))
                        .ThenBy(g => g.Name ?? string.Empty)
                        .ToList();
                    break;

                default:
                    sortedGames = _gameManager.Games.OrderBy(g => g.Name ?? string.Empty).ToList();
                    break;
            }

            _gameManager.Games.Clear();
            foreach (var game in sortedGames)
            {
                _gameManager.Games.Add(game);
            }

            Debug.WriteLine($"ApplySorting: Completed sorting");
        }

        private DateTime GetLastPlayedTime(GameInfo game)
        {
            if (string.IsNullOrEmpty(game.FolderName))
                return DateTime.MinValue;

            try
            {
                var gamePath = game.GetInstallPath(_gameManager.GamesFolder);
                var lastPlayedPath = Path.Combine(gamePath, "LastPlayed.txt");

                if (File.Exists(lastPlayedPath))
                {
                    var content = File.ReadAllText(lastPlayedPath).Trim();
                    if (DateTime.TryParseExact(content, "yyyy-MM-dd HH:mm:ss", null,
                        System.Globalization.DateTimeStyles.None, out DateTime lastPlayed))
                    {
                        return lastPlayed;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to read LastPlayed for {game.Name}: {ex.Message}");
            }

            return DateTime.MinValue;
        }

        private async void HideGame_Click(object sender, RoutedEventArgs e)
        {
            var menuItem = sender as MenuItem;
            var game = menuItem?.CommandParameter as GameInfo;

            if (game == null)
            {
                _ = ShowMessageBoxAsync("Unable to identify the selected app.", "Error");
                return;
            }

            try
            {
                _gameManager.ToggleUserHide(game);
                await _gameManager.LoadGamesAsync();
                ApplySorting();
            }
            catch (Exception ex)
            {
                _ = ShowMessageBoxAsync($"Failed to hide app: {ex.Message}", "Error");
            }
        }

        private async Task<bool> ShowMessageBoxAsync(string message, string title, bool isQuestion = false)
        {
            if (!isQuestion)
            {
                await ShowMessageBoxAsync(message, title);
                return true;
            }

            return await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop &&
                    desktop.MainWindow != null)
                {
                    bool result = false;
                    var messageBox = new Window
                    {
                        Title = title,
                        Width = 450,
                        Height = 170,
                        WindowStartupLocation = WindowStartupLocation.CenterOwner,
                        Content = new StackPanel
                        {
                            Margin = new Thickness(20),
                            Children =
                    {
                        new TextBlock
                        {
                            Text = message,
                            TextWrapping = TextWrapping.Wrap,
                            Margin = new Thickness(0, 0, 0, 20)
                        },
                        new StackPanel
                        {
                            Orientation = Orientation.Horizontal,
                            HorizontalAlignment = HorizontalAlignment.Center,
                            Children =
                            {
                                new Button
                                {
                                    Content = "Yes",
                                    Margin = new Thickness(0, 0, 10, 0),
                                    MinWidth = 80
                                },
                                new Button
                                {
                                    Content = "No",
                                    MinWidth = 80
                                }
                            }
                        }
                    }
                        }
                    };

                    if (((StackPanel)messageBox.Content).Children[1] is StackPanel buttonPanel &&
                        buttonPanel.Children[0] is Button yesButton &&
                        buttonPanel.Children[1] is Button noButton)
                    {
                        yesButton.Click += (s, e) =>
                        {
                            result = true;
                            messageBox.Close();
                        };

                        noButton.Click += (s, e) =>
                        {
                            result = false;
                            messageBox.Close();
                        };
                    }

                    await messageBox.ShowDialog(desktop.MainWindow);
                    return result;
                }
                return false;
            });
        }
        private void EnableGamepadCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            if (_settings != null)
            {
                _settings.EnableGamepadInput = true;
                _inputService?.SetGamepadEnabled(true);
                OnSettingChanged();
            }
        }

        private void EnableGamepadCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            if (_settings != null)
            {
                _settings.EnableGamepadInput = false;
                _inputService?.SetGamepadEnabled(false);
                OnSettingChanged();
            }
        }

        private void CloseAfterLaunchCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            if (_settings != null)
            {
                _settings.CloseAfterLaunch = true;
                OnPropertyChanged(nameof(CloseAfterLaunch));
                OnSettingChanged();
            }
        }

        private void CloseAfterLaunchCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            if (_settings != null)
            {
                _settings.CloseAfterLaunch = false;
                OnPropertyChanged(nameof(CloseAfterLaunch));
                OnSettingChanged();
            }
        }

        private void GitHubTokenTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_settings != null && sender is TextBox textBox)
            {
                _settings.GitHubApiToken = textBox.Text ?? string.Empty;
                OnSettingChanged();
            }
        }

        private void BackgroundPathTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_settings != null && sender is TextBox textBox)
            {
                _settings.BackgroundImagePath = textBox.Text ?? string.Empty;
                OnSettingChanged();
            }
        }

        private void LinuxWindowsLaunchCommandTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_settings != null && sender is TextBox textBox)
            {
                _settings.LinuxWindowsLaunchCommand = textBox.Text?.Trim() ?? string.Empty;
                OnSettingChanged();
            }
        }

        private void CrossOverPath_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_settings != null && sender is TextBox textBox)
            {
                _settings.CrossOverAppPath = textBox.Text?.Trim() ?? string.Empty;
                OnSettingChanged();
            }
        }

        private async void GameDataRequirements_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Control { Tag: GameInfo game })
                await ShowGameDataRequirementsAsync(game.Name, game.Repository);
        }

        private async Task ShowGameDataRequirementsAsync(string? name, string? repository)
        {
            var data = GameDataRequirements.Find(repository);
            var dialog = new Window { Title = $"ROM / disc requirements — {name}", Width = 560, MaxHeight = 720,
                SizeToContent = SizeToContent.Height, WindowStartupLocation = WindowStartupLocation.CenterOwner };
            var panel = new StackPanel { Margin = new Thickness(24), Spacing = 14 };
            panel.Children.Add(new TextBlock { Text = "Original game data required", FontSize = 20, FontWeight = FontWeight.Bold });
            panel.Children.Add(new TextBlock { Text = data.Details, TextWrapping = TextWrapping.Wrap });
            panel.Children.Add(new TextBlock { Text = "These requirements identify the original game data. Mac / Windows selects the port you download, not the original console. Check the guide for the release you install.", TextWrapping = TextWrapping.Wrap });
            var guide = new Button { Content = "Open official setup guide" };
            guide.Click += (_, _) => OpenUrl(data.Guide);
            panel.Children.Add(guide);
            var close = new Button { Content = "Close" };
            close.Click += (_, _) => dialog.Close();
            panel.Children.Add(close);
            dialog.Content = new ScrollViewer { Content = panel };
            await dialog.ShowDialog(this);
        }

        private async void ConfigureMacLaunch_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem { CommandParameter: GameInfo game })
                await ConfigureMacLaunchAsync(game);
        }

        private async Task ConfigureMacLaunchAsync(GameInfo game, string? setupNotice = null)
        {
            var compatibility = MacCompatibility.Find(game.Repository);
            bool nativeAvailable = false, windowsAvailable = false;
            string availabilityError = "";
            try
            {
                var releases = await game.FetchPublishedReleasesAsync(_gameManager.HttpClient);
                (nativeAvailable, windowsAvailable) = MacCompatibility.AvailablePlatforms(releases);
            }
            catch (Exception ex)
            {
                availabilityError = "Could not verify available releases. Try again before selecting a download platform. " + ex.Message;
            }
            var dialog = new Window
            {
                Title = $"Game version and launch — {game.Name}", Width = 620, MaxHeight = 850,
                SizeToContent = SizeToContent.Height, CanResize = false,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };
            var runner = new ComboBox { ItemsSource = new[] { "Steam / NotProton", "Direct CrossOver (simpler setup)" },
                SelectedIndex = game.MacWindowsRunner == "crossover" ? 1 : 0 };
            var bottle = new TextBox { Text = game.CrossOverBottle,
                Watermark = $"Default bottle: {_settings.CrossOverBottle}" };
            var tools = SteamNotProton.Tools();
            var tool = new ComboBox { ItemsSource = tools, SelectedItem = game.SteamTool };
            if (tool.SelectedItem == null && tools.Count == 1) tool.SelectedIndex = 0;
            var accounts = SteamNotProton.Accounts();
            var account = new ComboBox { ItemsSource = accounts, SelectedItem = game.SteamAccount };
            if (account.SelectedItem == null && accounts.Count == 1) account.SelectedIndex = 0;
            var gamePath = game.GetInstallPath(_gameManager.GamesFolder);
            var executables = Directory.Exists(gamePath) ? Directory.GetFiles(gamePath, "*", SearchOption.AllDirectories)
                .Where(p => p.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)).ToList() : new List<string>();
            var executable = new ComboBox { ItemsSource = executables, SelectedItem = game.SteamExecutable };
            if (executable.SelectedItem == null && executables.Count == 1) executable.SelectedIndex = 0;
            // Installed games can be configured even when release discovery is offline.
            windowsAvailable |= executables.Count > 0;
            var testing = new CheckBox { Content = "Allow testing unverified Windows builds",
                IsChecked = game.AllowCrossOverTesting };
            var platform = new ComboBox
            {
                ItemsSource = new[]
                {
                    new ComboBoxItem { Content = "Mac — native release available", IsEnabled = nativeAvailable, IsVisible = nativeAvailable },
                    new ComboBoxItem { Content = "Windows — Steam / NotProton or CrossOver", IsEnabled = windowsAvailable, IsVisible = windowsAvailable }
                },
                SelectedIndex = game.PreferWindowsBuild && windowsAvailable ? 1 : nativeAvailable ? 0 : windowsAvailable ? 1 : -1,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            var platformHelp = new TextBlock { TextWrapping = TextWrapping.Wrap };
            var windowsOptions = new StackPanel { Spacing = 12 };
            var message = new TextBlock { TextWrapping = TextWrapping.Wrap, MaxWidth = 550 };
            var save = new Button { Content = "Save download settings" };
            var prepare = new Button { Content = "Prepare Steam shortcut", IsEnabled = executables.Count > 0 };
            var readiness = new TextBlock { TextWrapping = TextWrapping.Wrap, FontWeight = FontWeight.Bold };
            var report = new Button { Content = "Record gameplay test…" };
            var cancel = new Button { Content = "Close" };
            cancel.Click += (_, _) => dialog.Close();
            async Task PersistAsync(bool register)
            {
                if (platform.SelectedIndex < 0 || (platform.SelectedIndex == 0 && !nativeAvailable) ||
                    (platform.SelectedIndex == 1 && !windowsAvailable))
                    throw new InvalidOperationException("Select a platform with an available download.");
                string selectedRunner = runner.SelectedIndex == 0 ? "notproton" : "crossover";
                string selectedBottle = bottle.Text?.Trim() ?? "";
                string selectedTool = tool.SelectedItem as string ?? "";
                string selectedAccount = account.SelectedItem as string ?? "";
                if (platform.SelectedIndex == 1 && selectedRunner == "notproton") SteamNotProton.ValidateSetup(selectedTool, selectedAccount);
                else if (platform.SelectedIndex == 1 && (testing.IsChecked == true || selectedBottle.Length > 0))
                {
                    string effective = !string.IsNullOrWhiteSpace(selectedBottle) ? selectedBottle :
                        !string.IsNullOrWhiteSpace(compatibility?.Bottle) ? compatibility.Bottle : _settings.CrossOverBottle;
                    _ = MacCompatibility.CrossOverStartInfo(_settings.CrossOverAppPath, effective,
                        Path.Combine(_gameManager.GamesFolder, "validation.exe"), _gameManager.GamesFolder);
                }
                var apps = await _gameManager.GetAppsAsync();
                var stored = apps.FirstOrDefault(a => string.Equals(a.Repository, game.Repository, StringComparison.OrdinalIgnoreCase));
                if (stored == null) throw new InvalidOperationException("Add this game to the library before configuring it.");
                ulong id = game.SteamShortcutId;
                string selectedExe = game.SteamExecutable;
                if (register)
                {
                    if (platform.SelectedIndex != 1 || selectedRunner != "notproton") throw new InvalidOperationException("Choose the Windows version and Steam / NotProton to prepare the shortcut.");
                    selectedExe = executable.SelectedItem as string ?? "";
                    id = SteamNotProton.Register(game.Name!, selectedExe, selectedTool, selectedAccount);
                }
                stored.CrossOverBottle = game.CrossOverBottle = selectedBottle;
                stored.AllowCrossOverTesting = game.AllowCrossOverTesting = testing.IsChecked == true;
                stored.MacWindowsRunner = game.MacWindowsRunner = selectedRunner;
                stored.SteamTool = game.SteamTool = selectedTool;
                stored.SteamAccount = game.SteamAccount = selectedAccount;
                stored.SteamShortcutId = game.SteamShortcutId = id;
                stored.SteamExecutable = game.SteamExecutable = selectedExe;
                stored.PreferWindowsBuild = game.PreferWindowsBuild = platform.SelectedIndex == 1;
                await _gameManager.SaveAppsAsync(apps);
                game.SelectedDownload = null;
                game.AvailableDownloads = null;
                if (register) game.SelectedExecutable = selectedExe;
            }
            save.Click += async (_, _) =>
            {
                save.IsEnabled = false;
                try
                {
                    await PersistAsync(false);
                    dialog.Close();
                }
                catch (Exception ex) { message.Text = ex.Message; }
                finally { save.IsEnabled = true; }
            };
            prepare.Click += async (_, _) =>
            {
                prepare.IsEnabled = false;
                try
                {
                    await PersistAsync(true);
                    UpdatePlatformChoice();
                    message.Text = "Steam shortcut prepared. Open Steam with the selected account, close this dialog, then click Launch again.";
                }
                catch (Exception ex) { message.Text = ex.Message; }
                finally { prepare.IsEnabled = true; }
            };
            report.Click += async (_, _) => await RecordMacGameplayAsync(game, dialog);
            var buttons = new WrapPanel { Orientation = Orientation.Horizontal };
            foreach (var button in new[] { save, report, cancel }) { button.Margin = new Thickness(0, 0, 8, 8); buttons.Children.Add(button); }
            var panel = new StackPanel { Margin = new Thickness(24), Spacing = 12 };
            if (!string.IsNullOrWhiteSpace(setupNotice))
                panel.Children.Add(new TextBlock { Text = setupNotice, TextWrapping = TextWrapping.Wrap, FontWeight = FontWeight.Bold });
            panel.Children.Add(new TextBlock { Text = "Which game version do you want to use?", FontSize = 18, FontWeight = FontWeight.Bold });
            panel.Children.Add(platform);
            panel.Children.Add(platformHelp);
            panel.Children.Add(windowsOptions);
            panel.Children.Add(new TextBlock { Text = GameDataRequirements.Find(game.Repository).Details, TextWrapping = TextWrapping.Wrap });
            var dataGuide = new Button { Content = "Open official game data guide" };
            dataGuide.Click += (_, _) => OpenUrl(GameDataRequirements.Find(game.Repository).Guide);
            panel.Children.Add(dataGuide);
            panel.Children.Add(new TextBlock { Text = "Your choice applies to downloads, updates, and launch. If that version is unavailable, the launcher will not switch platforms automatically. Changing this setting does not replace files already installed.", TextWrapping = TextWrapping.Wrap });
            panel.Children.Add(new TextBlock { Text = compatibility?.Requirements ?? "Check the project’s original game data and setup requirements.", TextWrapping = TextWrapping.Wrap });
            windowsOptions.Children.Add(new TextBlock { Text = "Run the Windows version with" });
            windowsOptions.Children.Add(runner);
            windowsOptions.Children.Add(new TextBlock { Text = "Direct CrossOver bottle (blank uses default)" }); windowsOptions.Children.Add(bottle);
            windowsOptions.Children.Add(new TextBlock { Text = "Steam / NotProton: installed tool" }); windowsOptions.Children.Add(tool);
            windowsOptions.Children.Add(new TextBlock { Text = "Steam account (local ID; use the same account when playing)" }); windowsOptions.Children.Add(account);
            windowsOptions.Children.Add(new TextBlock { Text = "Installed Windows executable" }); windowsOptions.Children.Add(executable);
            windowsOptions.Children.Add(testing);
            windowsOptions.Children.Add(readiness);
            windowsOptions.Children.Add(prepare);
            windowsOptions.Children.Add(message);
            void UpdatePlatformChoice()
            {
                bool windows = platform.SelectedIndex == 1;
                windowsOptions.IsVisible = windows;
                prepare.IsVisible = windows && runner.SelectedIndex == 0;
                bottle.IsVisible = windows && runner.SelectedIndex == 1;
                save.IsEnabled = platform.SelectedIndex >= 0;
                var chosenExe = executable.SelectedItem as string ?? "";
                bool prepared = SteamNotProton.MatchesPreparedExecutable(game.SteamShortcutId, game.SteamExecutable, chosenExe, game.Name!) &&
                    tool.SelectedItem as string == game.SteamTool && account.SelectedItem as string == game.SteamAccount;
                readiness.IsVisible = windows && runner.SelectedIndex == 0;
                readiness.Text = executables.Count == 0
                    ? "Step 1: save download settings and download the Windows build. Step 2: return here and prepare its Steam shortcut."
                    : prepared ? "Steam shortcut saved for this executable. Launch will verify the Steam account and tool mapping."
                    : "Setup required: close Steam, select the executable, tool and account, then click Prepare Steam shortcut. Saving download settings alone does not prepare launch.";
                platformHelp.Text = availabilityError.Length > 0 ? availabilityError
                    : platform.SelectedIndex < 0 ? "No supported Mac or Windows download was found. Check the project's releases."
                    : windows
                        ? (nativeAvailable ? "" : "No native macOS download is available. ") +
                          "The Windows build requires Steam / NotProton or CrossOver. Unverified builds require explicit testing opt-in; gameplay is not guaranteed."
                        : "A native macOS download is available. Check the game's setup requirements and any extra Mac libraries below.";
            }
            platform.SelectionChanged += (_, _) => UpdatePlatformChoice();
            runner.SelectionChanged += (_, _) => UpdatePlatformChoice();
            executable.SelectionChanged += (_, _) => UpdatePlatformChoice();
            tool.SelectionChanged += (_, _) => UpdatePlatformChoice();
            account.SelectionChanged += (_, _) => UpdatePlatformChoice();
            UpdatePlatformChoice();
            panel.Children.Add(new TextBlock { Text = MacGameplay.Summary(compatibility), TextWrapping = TextWrapping.Wrap });
            panel.Children.Add(buttons);
            dialog.Content = new ScrollViewer { Content = panel };
            await dialog.ShowDialog(this);
        }

        private async Task RecordMacGameplayAsync(GameInfo game, Window owner)
        {
            var path = game.GetInstallPath(_gameManager.GamesFolder);
            string Read(string file) => File.Exists(Path.Combine(path, file)) ? File.ReadAllText(Path.Combine(path, file)).Trim() : "";
            var dialog = new Window { Title = $"Gameplay — {game.Name}", Width = 610, MaxHeight = 800,
                SizeToContent = SizeToContent.Height, WindowStartupLocation = WindowStartupLocation.CenterOwner };
            var runner = new ComboBox { ItemsSource = new[] { "Native Mac", "Direct CrossOver", "Steam / NotProton" },
                SelectedIndex = game.MacWindowsRunner == "notproton" ? 2 : 1 };
            if (Directory.Exists(path) && MacCompatibility.FindExecutables(path, SearchOption.AllDirectories, out var needsWine).Count > 0 && !needsWine) runner.SelectedIndex = 0;
            var result = new ComboBox { ItemsSource = new[] { "Playable", "Playable with adjustments", "Unsupported" }, SelectedIndex = -1 };
            var release = new TextBox { Text = Read("version.txt"), Watermark = "Exact release (e.g. v1.0)" };
            var asset = new TextBox { Text = Read("installed-asset.txt"), Watermark = "Exact release asset filename" };
            var environment = new TextBox { Text = $"{System.Runtime.InteropServices.RuntimeInformation.OSDescription}, {System.Runtime.InteropServices.RuntimeInformation.OSArchitecture}; ",
                Watermark = "Mac model, runtime/version, Rosetta/FEX tool, graphics backend", TextWrapping = TextWrapping.Wrap };
            var evidence = new TextBox { AcceptsReturn = true, MinHeight = 100, TextWrapping = TextWrapping.Wrap,
                Watermark = "Observed gameplay: duration/section, FPS, controls, audio, failures, required adjustments; optional source link" };
            var message = new TextBlock { TextWrapping = TextWrapping.Wrap };
            var save = new Button { Content = "Save local report" };
            save.Click += (_, _) =>
            {
                try
                {
                    if (result.SelectedIndex < 0) throw new InvalidOperationException("Select the result observed during gameplay.");
                    if (runner.SelectedIndex == 2 && !SteamNotProton.Tools().Contains(game.SteamTool))
                        throw new InvalidOperationException("Save this game's installed NotProton tool before recording its gameplay report.");
                    MacGameplay.Save(game.Repository!, new MacGameplayReport
                    {
                        Runner = new[] { "native", "crossover", "notproton" }[runner.SelectedIndex],
                        Result = new[] { "playable", "adjustments", "unsupported" }[result.SelectedIndex],
                        Release = release.Text?.Trim() ?? "", AssetName = asset.Text?.Trim() ?? "",
                        Environment = environment.Text?.Trim() ?? "", Evidence = evidence.Text?.Trim() ?? "", TestedAt = DateTimeOffset.UtcNow,
                        PlatformFingerprint = MacGameplay.PlatformFingerprint,
                        RuntimeId = MacGameplay.RuntimeIdentity(new[] { "native", "crossover", "notproton" }[runner.SelectedIndex], game.SteamTool, _settings.CrossOverAppPath)
                    });
                    game.RefreshMacGameplayStatus();
                    _ = LoadAppCatalogAsync(forceRefresh: false);
                    dialog.Close();
                }
                catch (Exception ex) { message.Text = ex.Message; }
            };
            var panel = new StackPanel { Margin = new Thickness(24), Spacing = 12 };
            panel.Children.Add(new TextBlock { Text = "Record actual gameplay, beyond starting the game. Reports stay on this Mac; new releases need fresh tests. This is not Valve certification.", TextWrapping = TextWrapping.Wrap });
            foreach (var (label, control) in new (string, Control)[] { ("Tested launch method", runner), ("Result", result),
                ("Release", release), ("Asset", asset), ("Complete environment", environment), ("Report and adjustments", evidence) })
            { panel.Children.Add(new TextBlock { Text = label }); panel.Children.Add(control); }
            panel.Children.Add(message); panel.Children.Add(save);
            dialog.Content = new ScrollViewer { Content = panel };
            await dialog.ShowDialog(owner);
        }

        private void CrossOverBottle_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_settings != null && sender is TextBox textBox)
            {
                _settings.CrossOverBottle = textBox.Text?.Trim() ?? string.Empty;
                OnSettingChanged();
            }
        }

        private List<(string Category, List<CatalogEntry> Entries)>? _loadedCatalogCategories;
        private readonly HashSet<string> _catalogSelectedPlatforms = ["macos", "windows", "linux"];
        private bool _catalogIncludeUnknown = true;
        private bool _catalogLoadQueued;
        private CatalogViewState? _catalogView;

        private sealed record CatalogViewState(List<CatalogEntry> Entries,
            Dictionary<string, MacCompatibilityEntry> Compatibility, HashSet<string> LibraryRepos,
            WrapPanel Cards, TextBlock Count, TextBlock Footer, int Total);

        private void CatalogSearch_TextChanged(object? sender, TextChangedEventArgs e) => RefreshCatalogView();
        private void CatalogFilter_SelectionChanged(object? sender, SelectionChangedEventArgs e) => RefreshCatalogView();
        private void CatalogPlatform_CheckedChanged(object? sender, RoutedEventArgs e)
        {
            if (sender is not CheckBox { Tag: string key } checkBox) return;
            if (key == "unknown") _catalogIncludeUnknown = checkBox.IsChecked == true;
            else if (checkBox.IsChecked == true) _catalogSelectedPlatforms.Add(key);
            else _catalogSelectedPlatforms.Remove(key);
            if (this.FindControl<Button>("CatalogPlatformButton") is { } button)
                button.Content = $"Platforms · {_catalogSelectedPlatforms.Count + (_catalogIncludeUnknown ? 1 : 0)} selected ▾";
            RefreshCatalogView();
        }

        private void RefreshCatalogView()
        {
            if (_loadedCatalogCategories != null && this.FindControl<StackPanel>("AppCatalogContent") is { } content)
            {
                RenderCatalogCategories(content, _loadedCatalogCategories);
                if (this.FindControl<ScrollViewer>("AppBrowserTab") is { } scroll) scroll.Offset = new Vector(0, 0);
            }
        }

        private void Catalog_ScrollChanged(object? sender, ScrollChangedEventArgs e)
        {
            if (sender is not ScrollViewer scroll || !scroll.IsVisible || _catalogView is not { } view || _catalogLoadQueued ||
                !CatalogPresentation.ShouldLoadMore(scroll.Offset.Y, scroll.Viewport.Height, scroll.Extent.Height,
                    view.Cards.Children.Count, view.Entries.Count)) return;
            _catalogLoadQueued = true;
            Dispatcher.UIThread.Post(() =>
            {
                _catalogLoadQueued = false;
                if (ReferenceEquals(view, _catalogView) && scroll.IsVisible &&
                    CatalogPresentation.ShouldLoadMore(scroll.Offset.Y, scroll.Viewport.Height, scroll.Extent.Height,
                        view.Cards.Children.Count, view.Entries.Count)) AppendCatalogCards(view);
            }, DispatcherPriority.Background);
        }

        private void AppendCatalogCards(CatalogViewState view)
        {
            var end = Math.Min(view.Cards.Children.Count + 36, view.Entries.Count);
            for (int index = view.Cards.Children.Count; index < end; index++)
            {
                var entry = view.Entries[index];
                view.Compatibility.TryGetValue(entry.Repository, out var support);
                view.Cards.Children.Add(BuildCatalogCard(entry, view.LibraryRepos.Contains(entry.Repository), support));
            }
            view.Count.Text = $"Showing {end} of {view.Entries.Count} matching projects · {view.Total} total";
            view.Footer.Text = end < view.Entries.Count ? "Scroll for more projects"
                : $"All {view.Entries.Count} matching projects shown";
        }

        public int MacCatalogFilterIndex
        {
            get => Enum.IsDefined(_settings.CatalogFilter) ? (int)_settings.CatalogFilter : 0;
            set
            {
                if (!Enum.IsDefined(typeof(MacCatalogFilter), value) || MacCatalogFilterIndex == value) return;
                _settings.CatalogFilter = (MacCatalogFilter)value;
                OnPropertyChanged(nameof(MacCatalogFilterIndex));
                OnPropertyChanged(nameof(MacCatalogFilterDescription));
                OnSettingChanged();
                RefreshCatalogView();
            }
        }

        public string MacCatalogFilterDescription => (MacCatalogFilter)MacCatalogFilterIndex switch
        {
            MacCatalogFilter.Native => "Published macOS builds. Each project may require original game data or extra dependencies.",
            MacCatalogFilter.WindowsPlayable => "Windows builds with a current playable report for this Mac environment. Each card shows the working launch method.",
            MacCatalogFilter.Other => "Windows candidates and projects without confirmed Mac support. Choose a game to configure and try it yourself.",
            _ => "Every catalog entry, including native Mac builds, confirmed Windows builds and games you can try yourself."
        };

        private System.Threading.CancellationTokenSource? _gamePathUpdateCts;

        private async void GamePathTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_settings == null || sender is not TextBox textBox)
                return;

            // Cancel previous update
            var oldCts = _gamePathUpdateCts;
            _gamePathUpdateCts = new System.Threading.CancellationTokenSource();

            // Dispose old token after a delay
            if (oldCts != null)
            {
                var tokenToDispose = oldCts;
                _ = Task.Run(async () =>
                {
                    await Task.Delay(1000);
                    tokenToDispose.Dispose();
                });
            }

            try
            {
                await Task.Delay(500, _gamePathUpdateCts.Token);
                await UpdateGamePath(textBox.Text?.Trim() ?? string.Empty, textBox);
            }
            catch (OperationCanceledException)
            {
                // User is still typing
            }
        }

        private async Task UpdateGamePath(string newPath, TextBox textBox)
        {
            if (_settings.AppsPath == newPath)
                return;

            _settings.AppsPath = newPath;
            OnSettingChanged();

            if (_gameManager == null)
                return;

            if (!string.IsNullOrEmpty(newPath) && !Directory.Exists(newPath))
            {
                var result = await ShowMessageBoxAsync(
                    $"The directory '{newPath}' does not exist. Create it?",
                    "Directory Not Found",
                    true);

                if (!result)
                {
                    textBox.Text = _settings.AppsPath;
                    return;
                }
            }

            try
            {
                await _gameManager.UpdateGamesFolderAsync(_settings.AppsPath);
                await _gameManager.LoadGamesAsync();
                ApplySorting();
            }
            catch (Exception ex)
            {
                await ShowMessageBoxAsync($"Failed to update apps path: {ex.Message}", "Error");
                textBox.Text = _settings.AppsPath;
            }
        }

        private void CreateGitHubToken_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string githubTokenUrl = "https://github.com/settings/tokens/new?description=Github-Launcher+Token+for+increased+API+rate+limits";
                OpenUrl(githubTokenUrl);
            }
            catch (Exception ex)
            {
                _ = ShowMessageBoxAsync($"Failed to open GitHub token page: {ex.Message}", "Error");
            }
        }

        private void ClearGitHubToken_Click(object sender, RoutedEventArgs e)
        {
            if (_settings != null)
            {
                _settings.GitHubApiToken = string.Empty;
                if (GitHubTokenTextBox != null)
                    GitHubTokenTextBox.Text = string.Empty;
                OnSettingChanged();
            }
        }

        private void AppListRepositoryTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (sender is TextBox textBox && _settings != null)
            {
                _settings.AppListRepository = textBox.Text?.Trim() ?? "SirDiabo/GHLAppList";
                OnSettingChanged();
            }
        }

        private void EnableCommunityCatalog_Changed(object? sender, RoutedEventArgs e)
        {
            if (_settings == null || sender is not CheckBox checkbox) return;
            _settings.EnableCommunityCatalog = checkbox.IsChecked == true;
            OnSettingChanged();
        }

        private void ResetAppListRepository_Click(object sender, RoutedEventArgs e)
        {
            if (_settings != null)
            {
                _settings.AppListRepository = "SirDiabo/GHLAppList";
                if (AppListRepositoryTextBox != null)
                    AppListRepositoryTextBox.Text = "SirDiabo/GHLAppList";
                OnSettingChanged();
            }
        }

        private async void ClearGamePath_Click(object sender, RoutedEventArgs e)
        {
            if (_settings == null)
                return;

            try
            {
                _settings.AppsPath = string.Empty;
                if (GamePathTextBox != null)
                    GamePathTextBox.Text = string.Empty;
                OnSettingChanged();

                if (_gameManager != null)
                {
                    await _gameManager.UpdateGamesFolderAsync(string.Empty);
                    await _gameManager.LoadGamesAsync();
                    ApplySorting();
                }
            }
            catch (Exception ex)
            {
                _ = ShowMessageBoxAsync($"Failed to clear games path: {ex.Message}", "Error");
            }
        }

        private async void BrowseGamePath_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
                {
                    Title = "Select Apps Folder",
                    AllowMultiple = false
                });

                if (folders?.Count > 0)
                {
                    var selectedPath = folders[0].Path.LocalPath;

                    if (!Directory.Exists(selectedPath))
                    {
                        _ = ShowMessageBoxAsync("Selected folder does not exist.", "Invalid Selection");
                        return;
                    }

                    if (_settings != null)
                    {
                        _settings.AppsPath = selectedPath;

                        if (GamePathTextBox != null)
                            GamePathTextBox.Text = selectedPath;

                        OnSettingChanged();

                        if (_gameManager != null)
                        {
                            await _gameManager.UpdateGamesFolderAsync(selectedPath);
                            await _gameManager.LoadGamesAsync();
                            ApplySorting();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _ = ShowMessageBoxAsync($"Failed to select folder: {ex.Message}", "Error");
            }
        }

        private async void ClearIconCache_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await _gameManager.ClearIconCacheAsync();
            }
            catch (Exception ex)
            {
                await ShowMessageBoxAsync($"Failed to clear icon cache: {ex.Message}", "Error");
            }
        }

        private async void SelectBackgroundImage_Click(object sender, RoutedEventArgs e)
        {
            var storageProvider = StorageProvider;
            var file = await storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Select Background Image",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
            new FilePickerFileType("Image Files")
            {
                Patterns = new[] { "*.png", "*.jpg", "*.jpeg", "*.gif", "*.bmp", "*.webp" }
            }
        }
            });

            if (file.Count > 0)
            {
                var selectedFile = file[0];
                BackgroundImagePath = selectedFile.Path.LocalPath;
                _settings.BackgroundImagePath = BackgroundImagePath;

                AppSettings.Save(_settings);
            }
        }

        private void ClearBackgroundImage_Click(object sender, RoutedEventArgs e)
        {
            BackgroundImagePath = string.Empty;
            _settings.BackgroundImagePath = string.Empty;
            AppSettings.Save(_settings);
        }

        private void BackgroundOpacitySlider_ValueChanged(object sender, Avalonia.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            if (sender is Slider slider)
            {
                BackgroundOpacity = (float)slider.Value;
                _settings.BackgroundOpacity = BackgroundOpacity;
                AppSettings.Save(_settings);
            }
        }

        private async void SelectLauncherMusic_Click(object sender, RoutedEventArgs e)
        {
            var storageProvider = StorageProvider;
            var file = await storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Select Launcher Music",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
            new FilePickerFileType("Audio Files")
            {
                Patterns = new[] { "*.mp3", "*.wav", "*.ogg", "*.flac", "*.m4a", "*.wma", "*.aac" }
            }
        }
            });

            if (file.Count > 0)
            {
                var selectedFile = file[0];
                LauncherMusicPath = selectedFile.Path.LocalPath;
                _settings.LauncherMusicPath = LauncherMusicPath;
                AppSettings.Save(_settings);

                PlayLauncherMusic(LauncherMusicPath);
            }
        }

        private void ClearLauncherMusic_Click(object sender, RoutedEventArgs e)
        {
            StopLauncherMusic();
            LauncherMusicPath = string.Empty;
            _settings.LauncherMusicPath = string.Empty;
            AppSettings.Save(_settings);
        }

        // Apps Manager Tab Properties
        public ObservableCollection<GameInfo> ManagerGames { get; set; } = [];
        private GameInfo? _selectedGame;
        public GameInfo? SelectedGame
        {
            get => _selectedGame;
            set
            {
                if (_selectedGame != value)
                {
                    _selectedGame = value;
                    OnPropertyChanged(nameof(SelectedGame));
                    OnPropertyChanged(nameof(CanAddGame));
                    OnPropertyChanged(nameof(CanUpdateGame));
                    OnPropertyChanged(nameof(IsEditingCustomGame));
                    OnPropertyChanged(nameof(CanEditAllFields));
                    UpdateFormFieldsEnabled();
                }
            }
        }
        private string _validationStatus = string.Empty;
        public string ValidationStatus
        {
            get => _validationStatus;
            set
            {
                if (_validationStatus != value)
                {
                    _validationStatus = value;
                    OnPropertyChanged(nameof(ValidationStatus));
                    OnPropertyChanged(nameof(ValidationStatusColor));
                }
            }
        }
        public IBrush ValidationStatusColor => string.IsNullOrEmpty(_validationStatus) ? Brushes.Transparent :
            _validationStatus.Contains("Error") ? new SolidColorBrush(Color.Parse("#ef4444")) :
            _validationStatus.Contains("Warning") ? new SolidColorBrush(Color.Parse("#f59e0b")) :
            new SolidColorBrush(Color.Parse("#10b981"));
        public bool CanAddGame => SelectedGame == null && !string.IsNullOrEmpty(_selectedGame?.Name) &&
            !string.IsNullOrEmpty(_selectedGame?.Repository) && !string.IsNullOrEmpty(_selectedGame?.FolderName);
        public bool CanUpdateGame => SelectedGame != null && !string.IsNullOrEmpty(_selectedGame?.Name) &&
            !string.IsNullOrEmpty(_selectedGame?.Repository) && !string.IsNullOrEmpty(_selectedGame?.FolderName);
        
        // App editing state
        public bool IsEditingCustomGame => SelectedGame != null;
        public bool CanEditAllFields => SelectedGame != null;

        // Method to update form field enabled state
        private void UpdateFormFieldsEnabled()
        {
            if (SelectedGame == null) return;

            var nameBox = this.FindControl<TextBox>("NewGameNameTextBox");
            var repoBox = this.FindControl<TextBox>("NewGameRepoTextBox");
            var folderBox = this.FindControl<TextBox>("NewGameFolderTextBox");
            var iconBox = this.FindControl<TextBox>("NewGameIconTextBox");
            var isCustomBox = this.FindControl<CheckBox>("IsCustomCheckBox");
            var isExperimentalBox = this.FindControl<CheckBox>("IsExperimentalCheckBox");
            if (repoBox != null) repoBox.IsEnabled = true;
            if (folderBox != null) folderBox.IsEnabled = true;
            if (isCustomBox != null) isCustomBox.IsEnabled = false;
            if (isExperimentalBox != null) isExperimentalBox.IsEnabled = false;

            // Name and icon URL can always be edited
            if (nameBox != null) nameBox.IsEnabled = true;
            if (iconBox != null) iconBox.IsEnabled = true;
        }

        // Apps Manager Event Handlers
        private async void AddGame_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedGame == null || string.IsNullOrEmpty(SelectedGame.Name) ||
                string.IsNullOrEmpty(SelectedGame.Repository) || string.IsNullOrEmpty(SelectedGame.FolderName))
            {
                await ShowMessageBoxAsync("Please fill in all required fields (Name, Repository, Folder Name).", "Validation Error");
                return;
            }

            try
            {
                var gamesData = await LoadGamesFromJsonAsync();
                var apps = gamesData.ToList();

                if (apps.Any(g => g.Name == SelectedGame.Name || g.Repository == SelectedGame.Repository))
                {
                    await ShowMessageBoxAsync("An app with this name or repository already exists.", "Duplicate App");
                    return;
                }

                var newGame = new GameInfo
                {
                    Name = SelectedGame.Name,
                    Repository = SelectedGame.Repository,
                    FolderName = SelectedGame.FolderName,
                    InstallPath = SelectedGame.InstallPath,
                    GameIconUrl = SelectedGame.GameIconUrl,
                    IsCustom = true,
                    IsExperimental = false,
                    GameManager = _gameManager
                };

                apps.Add(newGame);
                await SaveGamesToJsonAsync(apps);
                await LoadGamesManagerAsync();
                ClearGameForm_Click(sender, e);
                await ShowMessageBoxAsync($"App '{newGame.Name}' added successfully.", "App Added");
            }
            catch (Exception ex)
            {
                await ShowMessageBoxAsync($"Failed to add app: {ex.Message}", "Error");
            }
        }

        private async void UpdateGame_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedGame == null || string.IsNullOrEmpty(SelectedGame.Name) ||
                string.IsNullOrEmpty(SelectedGame.Repository) || string.IsNullOrEmpty(SelectedGame.FolderName))
            {
                await ShowMessageBoxAsync("Please fill in all required fields (Name, Repository, Folder Name).", "Validation Error");
                return;
            }

            try
            {
                var gamesData = await LoadGamesFromJsonAsync();
                var appToUpdate = gamesData.FirstOrDefault(g => g.Repository == SelectedGame.Repository) ??
                    gamesData.FirstOrDefault(g => g.Name == SelectedGame.Name);

                if (appToUpdate == null)
                {
                    await ShowMessageBoxAsync("App not found.", "Error");
                    return;
                }
                if (gamesData.Any(g => !ReferenceEquals(g, appToUpdate) && (g.Name == SelectedGame.Name || g.Repository == SelectedGame.Repository)))
                {
                    await ShowMessageBoxAsync("An app with this name or repository already exists.", "Duplicate App");
                    return;
                }

                // Update the game properties
                appToUpdate.Name = SelectedGame.Name;
                appToUpdate.Repository = SelectedGame.Repository;
                appToUpdate.FolderName = SelectedGame.FolderName;
                appToUpdate.InstallPath = SelectedGame.InstallPath;
                appToUpdate.GameIconUrl = SelectedGame.GameIconUrl;
                await SaveGamesToJsonAsync(gamesData);

                // Refresh the main game list and manager
                await _gameManager.LoadGamesAsync();
                ApplySorting();
                await LoadGamesManagerAsync();
                ClearGameForm_Click(sender, e);
                await ShowMessageBoxAsync($"App '{appToUpdate.Name}' updated successfully.", "App Updated");
            }
            catch (Exception ex)
            {
                await ShowMessageBoxAsync($"Failed to update app: {ex.Message}", "Error");
            }
        }

        private async void DeleteGame_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            var game = button?.Tag as GameInfo;

            if (game == null)
            {
                await ShowMessageBoxAsync("Apps can be deleted from this list.", "Error");
                return;
            }

            var result = await ShowMessageBoxAsync($"Are you sure you want to delete '{game.Name}'?", "Confirm Deletion", true);
            if (!result) return;

            try
            {
                var gamesData = await LoadGamesFromJsonAsync();
                var apps = gamesData.Where(g => g.Name != game.Name).ToList();

                await SaveGamesToJsonAsync(apps);
                await LoadGamesManagerAsync();
                await ShowMessageBoxAsync($"App '{game.Name}' deleted successfully.", "App Deleted");
            }
            catch (Exception ex)
            {
                await ShowMessageBoxAsync($"Failed to delete app: {ex.Message}", "Error");
            }
        }

        private async void EditGame_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            var game = button?.Tag as GameInfo;

            if (game == null)
            {
                await ShowMessageBoxAsync("Apps can be edited from this list.", "Error");
                return;
            }

            SelectedGame = new GameInfo
            {
                Name = game.Name,
                Repository = game.Repository,
                FolderName = game.FolderName,
                InstallPath = game.InstallPath,
                GameIconUrl = game.GameIconUrl,
                IsCustom = game.IsCustom,
                IsExperimental = game.IsExperimental
            };
        }

        private void ClearGameForm_Click(object sender, RoutedEventArgs e)
        {
            SelectedGame = null;
        }

        private async void ImportGames_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = "Import Apps JSON",
                    FileTypeFilter = new[] { new FilePickerFileType("JSON Files") { Patterns = new[] { "*.json" } } },
                    AllowMultiple = false
                });

                if (files?.Count > 0)
                {
                    var importedData = await File.ReadAllTextAsync(files[0].Path.LocalPath);
                    var gamesData = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(importedData);

                    if (gamesData != null && (gamesData.ContainsKey("apps") || gamesData.ContainsKey("standard") || gamesData.ContainsKey("experimental") || gamesData.ContainsKey("custom")))
                    {
                        await SaveGamesToJsonAsync(gamesData);
                        await LoadGamesManagerAsync();
                        await ShowMessageBoxAsync("Apps imported successfully.", "Import Complete");
                    }
                    else
                    {
                        await ShowMessageBoxAsync("Invalid apps.json format.", "Import Error");
                    }
                }
            }
            catch (Exception ex)
            {
                await ShowMessageBoxAsync($"Failed to import apps: {ex.Message}", "Error");
            }
        }

        private async void ExportGames_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var gamesData = await LoadGamesFromJsonAsync();
                var exportData = new
                {
                    apps = gamesData.Select(SerializeGame).ToList()
                };

                var options = new JsonSerializerOptions { WriteIndented = true };
                var exportPath = Path.Combine(LauncherPaths.DataDirectory, "apps_export.json");
                await File.WriteAllTextAsync(exportPath, JsonSerializer.Serialize(exportData, options));

                await ShowMessageBoxAsync($"Apps exported to {exportPath}", "Export Complete");
            }
            catch (Exception ex)
            {
                await ShowMessageBoxAsync($"Failed to export apps: {ex.Message}", "Error");
            }
        }

        private async void ValidateGames_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var gamesData = await LoadGamesFromJsonAsync();
                var issues = new List<string>();

                foreach (var game in gamesData)
                {
                    if (string.IsNullOrEmpty(game.Name)) issues.Add($"App '{game.Name}' has empty name");
                    if (string.IsNullOrEmpty(game.Repository)) issues.Add($"App '{game.Name}' has empty repository");
                    if (string.IsNullOrEmpty(game.FolderName)) issues.Add($"App '{game.Name}' has empty folder name");
                    if (game.GameIconUrl != null && !Uri.TryCreate(game.GameIconUrl, UriKind.Absolute, out _))
                        issues.Add($"App '{game.Name}' has invalid icon URL");
                }

                if (issues.Count == 0)
                {
                    ValidationStatus = "All apps are valid.";
                }
                else
                {
                    ValidationStatus = $"Found {issues.Count} issue(s):\n{string.Join("\n", issues.Take(5))}";
                    if (issues.Count > 5) ValidationStatus += $"\n... and {issues.Count - 5} more";
                }
            }
            catch (Exception ex)
            {
                ValidationStatus = $"Validation error: {ex.Message}";
            }
        }
        private async Task<List<GameInfo>> LoadGamesFromJsonAsync()
        {
            var appsPath = Path.Combine(LauncherPaths.DataDirectory, "apps.json");
            var legacyPath = Path.Combine(LauncherPaths.DataDirectory, "games.json");
            var sourcePath = File.Exists(appsPath) ? appsPath : legacyPath;
            if (!File.Exists(sourcePath)) return [];

            var json = await File.ReadAllTextAsync(sourcePath);
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            var apps = new List<GameInfo>();
            if (root.ValueKind == JsonValueKind.Array)
            {
                apps.AddRange(ParseGameArray(root, false, true));
            }
            else
            {
                if (root.TryGetProperty("apps", out var appsArray))
                    apps.AddRange(ParseGameArray(appsArray, false, true));
                foreach (var legacySection in new[] { "standard", "experimental", "custom" })
                {
                    if (root.TryGetProperty(legacySection, out var legacyArray))
                        apps.AddRange(ParseGameArray(legacyArray, false, true));
                }
            }

            return apps
                .GroupBy(app => app.Repository ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToList();
        }

        private List<GameInfo> ParseGameArray(JsonElement array, bool isExperimental, bool isCustom)
        {
            var apps = new List<GameInfo>();
            foreach (var element in array.EnumerateArray())
            {
                var app = new GameInfo
                {
                    Name = element.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "",
                    Repository = element.TryGetProperty("repository", out var r) ? r.GetString() ?? "" : "",
                    FolderName = element.TryGetProperty("folderName", out var f) ? f.GetString() ?? "" : "",
                    InstallPath = element.TryGetProperty("installPath", out var installPath) ? installPath.GetString() : null,
                    GameIconUrl = GetConfiguredIconUrl(element),
                    PreferredVersion = element.TryGetProperty("preferredVersion", out var preferredVersion) ? preferredVersion.GetString() : null,
                    SkippedUpdateVersion = element.TryGetProperty("skippedUpdateVersion", out var skippedUpdateVersion) ? skippedUpdateVersion.GetString() : null,
                    CrossOverBottle = element.TryGetProperty("crossOverBottle", out var bottle) ? bottle.GetString() ?? "" : "",
                    AllowCrossOverTesting = element.TryGetProperty("allowCrossOverTesting", out var testing) && testing.ValueKind == JsonValueKind.True,
                        MacWindowsRunner = element.TryGetProperty("macWindowsRunner", out var macWindowsRunner) ? macWindowsRunner.GetString() ?? "crossover" : "crossover",
                        SteamAccount = element.TryGetProperty("steamAccount", out var steamAccount) ? steamAccount.GetString() ?? "" : "",
                        SteamTool = element.TryGetProperty("steamTool", out var steamTool) ? steamTool.GetString() ?? "" : "",
                        SteamExecutable = element.TryGetProperty("steamExecutable", out var steamExecutable) ? steamExecutable.GetString() ?? "" : "",
                        PreferWindowsBuild = element.TryGetProperty("preferWindowsBuild", out var preferWindowsBuild) && preferWindowsBuild.ValueKind == JsonValueKind.True,
                        SteamShortcutId = element.TryGetProperty("steamShortcutId", out var steamId) && steamId.TryGetUInt64(out var id) ? id : 0,
                    IsExperimental = false,
                    IsCustom = true,
                    GameManager = _gameManager
                };
                apps.Add(app);
            }
            return apps;
        }

        private static string? GetConfiguredIconUrl(JsonElement element)
        {
            if (element.TryGetProperty("appIconUrl", out var appIconUrl) && appIconUrl.ValueKind != JsonValueKind.Null)
                return appIconUrl.GetString();
            if (element.TryGetProperty("gameIconUrl", out var gameIconUrl) && gameIconUrl.ValueKind != JsonValueKind.Null)
                return gameIconUrl.GetString();
            return null;
        }

        private static object SerializeGame(GameInfo game)
        {
            return new
            {
                name = game.Name,
                repository = game.Repository,
                folderName = game.FolderName,
                installPath = game.InstallPath,
                appIconUrl = game.GameIconUrl,
                preferredVersion = game.PreferredVersion,
                skippedUpdateVersion = game.SkippedUpdateVersion,
                crossOverBottle = game.CrossOverBottle,
                allowCrossOverTesting = game.AllowCrossOverTesting,
                macWindowsRunner = game.MacWindowsRunner,
                steamAccount = game.SteamAccount,
                steamTool = game.SteamTool,
                steamExecutable = game.SteamExecutable,
                preferWindowsBuild = game.PreferWindowsBuild,
                steamShortcutId = game.SteamShortcutId
            };
        }
        private async Task SaveGamesToJsonAsync(List<GameInfo> appsToSave)
        {
            var appsPath = Path.Combine(LauncherPaths.DataDirectory, "apps.json");
            var data = new
            {
                apps = appsToSave.Select(SerializeGame).ToList()
            };

            var options = new JsonSerializerOptions { WriteIndented = true };
            await File.WriteAllTextAsync(appsPath, JsonSerializer.Serialize(data, options));
        }

        private async Task SaveGamesToJsonAsync(Dictionary<string, JsonElement> gamesData)
        {
            var imported = new List<GameInfo>();
            if (gamesData.TryGetValue("apps", out var appsArray))
                imported.AddRange(ParseGameArray(appsArray, false, true));
            foreach (var legacySection in new[] { "standard", "experimental", "custom" })
            {
                if (gamesData.TryGetValue(legacySection, out var legacyArray))
                    imported.AddRange(ParseGameArray(legacyArray, false, true));
            }
            await SaveGamesToJsonAsync(imported);
        }
        private async Task PersistGameVersionPreferencesAsync(GameInfo game, string? preferredVersion, string? skippedUpdateVersion)
        {
            game.SetVersionPreferences(preferredVersion, skippedUpdateVersion);

            var allGames = await LoadGamesFromJsonAsync();
            var matchingGame = allGames.FirstOrDefault(g =>
                !string.IsNullOrWhiteSpace(g.Repository) &&
                g.Repository.Equals(game.Repository, StringComparison.OrdinalIgnoreCase));

            if (matchingGame == null)
                return;

            matchingGame.PreferredVersion = game.PreferredVersion;
            matchingGame.SkippedUpdateVersion = game.SkippedUpdateVersion;

            await SaveGamesToJsonAsync(allGames);
        }

        private async Task PersistGameInstallLocationAsync(GameInfo game)
        {
            var allGames = await LoadGamesFromJsonAsync();
            var matchingGame = allGames.FirstOrDefault(g =>
                !string.IsNullOrWhiteSpace(g.Repository) &&
                g.Repository.Equals(game.Repository, StringComparison.OrdinalIgnoreCase));

            if (matchingGame == null)
                return;

            matchingGame.FolderName = game.FolderName;
            matchingGame.InstallPath = game.InstallPath;

            await SaveGamesToJsonAsync(allGames);
        }

        private async Task LoadGamesManagerAsync()
        {
            try
            {
                var gamesData = await LoadGamesFromJsonAsync();
                ManagerGames.Clear();
                foreach (var game in gamesData)
                {
                    ManagerGames.Add(game);
                }
                ValidateGames_Click(null, null);
            }
            catch (Exception ex)
            {
                ValidationStatus = $"Failed to load apps: {ex.Message}";
            }
        }

        private void GameNameTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            ValidateGameForm();
        }

        private void GameRepoTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            ValidateGameForm();
        }

        private void GameFolderTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            ValidateGameForm();
        }

        private void GameIconTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            ValidateGameForm();
        }

        private void ValidateGameForm()
        {
            if (SelectedGame == null) return;

            var isValid = !string.IsNullOrEmpty(SelectedGame.Name) &&
                         !string.IsNullOrEmpty(SelectedGame.Repository) &&
                         !string.IsNullOrEmpty(SelectedGame.FolderName);

            // Update validation status
            if (string.IsNullOrEmpty(SelectedGame.Name))
            {
                ValidationStatus = "Error: App name is required";
            }
            else if (string.IsNullOrEmpty(SelectedGame.Repository))
            {
                ValidationStatus = "Error: Repository is required";
            }
            else if (string.IsNullOrEmpty(SelectedGame.FolderName))
            {
                ValidationStatus = "Error: Folder name is required";
            }
            else if (!Uri.TryCreate(SelectedGame.Repository, UriKind.Absolute, out var repoUri) || 
                     (repoUri.Scheme != Uri.UriSchemeHttp && repoUri.Scheme != Uri.UriSchemeHttps))
            {
                ValidationStatus = "Warning: Repository should be a valid URL";
            }
            else if (!IsValidFolderName(SelectedGame.FolderName))
            {
                ValidationStatus = "Warning: Folder name contains invalid characters";
            }
            else
            {
                ValidationStatus = "All fields are valid";
            }

            OnPropertyChanged(nameof(CanAddGame));
            OnPropertyChanged(nameof(CanUpdateGame));
        }

        private bool IsValidFolderName(string folderName)
        {
            if (string.IsNullOrWhiteSpace(folderName))
                return false;

            // Check for invalid characters
            var invalidChars = Path.GetInvalidFileNameChars();
            if (folderName.IndexOfAny(invalidChars) >= 0)
                return false;

            // Check for reserved names
            var reservedNames = new[] { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9" };
            if (reservedNames.Contains(folderName.ToUpper()))
                return false;

            return true;
        }

        private async void DeleteGameFromManager_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            var game = button?.Tag as GameInfo;

            if (game == null)
            {
                await ShowMessageBoxAsync("Apps can be deleted from this list.", "Error");
                return;
            }

            var result = await ShowMessageBoxAsync($"Are you sure you want to delete '{game.Name}'?", "Confirm Deletion", true);
            if (!result) return;

            try
            {
                var gamesData = await LoadGamesFromJsonAsync();
                var apps = gamesData.Where(g => g.Name != game.Name).ToList();

                await SaveGamesToJsonAsync(apps);
                await LoadGamesManagerAsync();
                await ShowMessageBoxAsync($"App '{game.Name}' deleted successfully.", "App Deleted");
            }
            catch (Exception ex)
            {
                await ShowMessageBoxAsync($"Failed to delete app: {ex.Message}", "Error");
            }
        }

        private void ManageGamesButton_Click(object sender, RoutedEventArgs e)
        {

            if (_isGamesManagerOpen)
            {
                CloseManageGames_Click(sender, e);
                return;
            }

            // Hide other panels
            SettingsPanel.IsVisible = false;
            ChangelogPanel.IsVisible = false;

            // Show Manage Apps panel
            _isGamesManagerOpen = true;
            var manageGamesPanel = this.FindControl<Border>("ManageGamesPanel");
            if (manageGamesPanel != null)
            {
                manageGamesPanel.IsVisible = true;
            }

            // Update header text
            HeaderTitleText.Text = "Manage Apps";

            // Initialize tabs
            SwitchToManageGamesTab(null, null);

            // Load apps from apps.json
            LoadGamesFromJson();
        }

        private async void LoadGamesFromJson()
        {
            try
            {
                var games = await LoadGamesFromJsonAsync();
                var settings = AppSettings.Load();
                UpdateGamesListControl("CustomGamesListControl", games, settings);
            }
            catch (Exception ex)
            {
                _ = ShowMessageBoxAsync($"Error loading apps: {ex.Message}", "Error");
            }
        }

        private void UpdateGamesListControl(string controlName, List<GameInfo> games, AppSettings settings)
        {
            var gamesListControl = this.FindControl<ItemsControl>(controlName);
            if (gamesListControl != null)
            {
                var gameViewModels = games.Select(g => new
                {
                    Name = g.Name,
                    Repository = g.Repository,
                    FolderName = g.FolderName,
                    InstallPath = g.InstallPath,
                    GameIconUrl = g.GameIconUrl,
                    IconUrl = g.IconUrl,
                    IsInstalled = !string.IsNullOrEmpty(g.FolderName) && Directory.Exists(g.GetInstallPath(_gameManager.GamesFolder)),
                    CanRemove = true,
                    HideGameLabel = _gameManager.IsManuallyHidden(g) ? "Unhide" : "Hide",
                    GameInfoRef = g
                }).ToList();

                gamesListControl.ItemsSource = gameViewModels;
            }
        }

        private void ToggleHideGame_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.Tag is not object gameData)
                return;

            var gameInfoProp = gameData.GetType().GetProperty("GameInfoRef");
            var game = gameInfoProp?.GetValue(gameData) as GameInfo;

            if (game == null)
                return;

            _gameManager.ToggleUserHide(game);
            LoadGamesFromJson();
        }

        private void SwitchToManageGamesTab(object sender, RoutedEventArgs e)
        {
            var manageGamesTab = this.FindControl<ScrollViewer>("ManageGamesTab");
            var createEditTab = this.FindControl<ScrollViewer>("CreateEditTab");
            var appBrowserTab = this.FindControl<ScrollViewer>("AppBrowserTab");

            if (manageGamesTab != null && createEditTab != null)
            {
                manageGamesTab.IsVisible = true;
                createEditTab.IsVisible = false;
                if (appBrowserTab != null) appBrowserTab.IsVisible = false;

                // Update form state
                var formTitle = this.FindControl<TextBlock>("FormTitleText");
                var createEditButton = this.FindControl<Button>("CreateEditButton");

                if (formTitle != null) formTitle.Text = "Create New Entry";
                if (createEditButton != null) createEditButton.Content = "Create Entry";

                ClearForm();
            }
        }

        private void SwitchToCreateEditTab(object sender, RoutedEventArgs e)
        {
            var manageGamesTab = this.FindControl<ScrollViewer>("ManageGamesTab");
            var createEditTab = this.FindControl<ScrollViewer>("CreateEditTab");
            var appBrowserTab = this.FindControl<ScrollViewer>("AppBrowserTab");

            if (manageGamesTab != null && createEditTab != null)
            {
                manageGamesTab.IsVisible = false;
                createEditTab.IsVisible = true;
                if (appBrowserTab != null) appBrowserTab.IsVisible = false;

                var cancelButton = this.FindControl<Button>("CancelButton");
                if (cancelButton != null) cancelButton.IsVisible = true;
            }
        }

        private async void CreateNewEntry_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var name = this.FindControl<TextBox>("NewGameNameTextBox")?.Text?.Trim();
                var repository = this.FindControl<TextBox>("NewGameRepoTextBox")?.Text?.Trim();
                var folderName = this.FindControl<TextBox>("NewGameFolderTextBox")?.Text?.Trim();
                var iconUrl = this.FindControl<TextBox>("NewGameIconTextBox")?.Text?.Trim();

                if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(repository) || string.IsNullOrEmpty(folderName))
                {
                    _ = ShowMessageBoxAsync("Please fill in all required fields (Name, Repository, Folder Name)", "Validation Error");
                    return;
                }

                var games = await LoadGamesFromJsonAsync();

                if (!string.IsNullOrEmpty(_editingGameRepository))
                {
                    // Update Existing Record
                    var appToUpdate = games.FirstOrDefault(g => g.Repository == _editingGameRepository);
                    if (appToUpdate == null)
                    {
                        _ = ShowMessageBoxAsync("Could not find the app to update.", "Error");
                        return;
                    }

                    // Verify a different app does not already have the new name
                    if (name != _editingGameName && games.Any(g => g.Name == name))
                    {
                        _ = ShowMessageBoxAsync("An app with this name already exists.", "Duplicate Name");
                        return;
                    }

                    // Update folder name if changed, if folder exists
                    if (appToUpdate.FolderName != folderName && !string.IsNullOrEmpty(appToUpdate.FolderName))
                    {
                        var oldPath = Path.Combine(_settings.AppsPath, appToUpdate.FolderName);
                        var newPath = Path.Combine(_settings.AppsPath, folderName);

                        if (Directory.Exists(oldPath))
                        {
                            try
                            {
                                Directory.Move(oldPath, newPath);
                                System.Diagnostics.Debug.WriteLine($"Renamed folder: {appToUpdate.FolderName} -> {folderName}");
                            }
                            catch (Exception ex)
                            {
                                System.Diagnostics.Debug.WriteLine($"Failed to rename folder {appToUpdate.FolderName}: {ex.Message}");
                                _ = ShowMessageBoxAsync($"Failed to rename folder {appToUpdate.FolderName}", $"{ex.Message}");
                            }
                        }
                    }

                    appToUpdate.Name = name;
                    appToUpdate.FolderName = folderName;
                    appToUpdate.GameIconUrl = iconUrl;

                    await SaveGamesToJsonAsync(games);
                    _ = ShowMessageBoxAsync("app entry updated successfully", "App Updated");
                }
                else
                {
                    // Create New Record
                    if (games.Any(g => g.Name == name || g.Repository == repository || g.FolderName == folderName))
                    {
                        _ = ShowMessageBoxAsync("An app with this name, repository, or folder name already exists", "Duplicate App");
                        return;
                    }

                    var newGame = new GameInfo
                    {
                        Name = name,
                        Repository = repository,
                        FolderName = folderName,
                        GameIconUrl = iconUrl,
                        IsCustom = true,
                        IsExperimental = false
                    };

                    games.Add(newGame);
                    await SaveGamesToJsonAsync(games);
                    _ = ShowMessageBoxAsync("New app entry created successfully", "App Added");
                }

                LoadGamesFromJson();
                ClearForm();
                SwitchToManageGamesTab(null, null);
            }
            catch (Exception ex)
            {
                _ = ShowMessageBoxAsync($"Error saving app entry: {ex.Message}", "Error");
            }
        }

        private void EditGameEntry_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is var gameData)
            {
                try
                {
                    var nameProp = gameData.GetType().GetProperty("Name");
                    var repoProp = gameData.GetType().GetProperty("Repository");
                    var folderProp = gameData.GetType().GetProperty("FolderName");
                    var iconProp = gameData.GetType().GetProperty("GameIconUrl");

                    var name = nameProp?.GetValue(gameData) as string;
                    var repository = repoProp?.GetValue(gameData) as string;
                    var folderName = folderProp?.GetValue(gameData) as string;
                    var iconUrl = iconProp?.GetValue(gameData) as string;

                    if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(repository) || string.IsNullOrEmpty(folderName))
                    {
                        _ = ShowMessageBoxAsync("Unable to extract app information for editing.", "Error");
                        return;
                    }

                    SwitchToCreateEditTab(null, null);

                    var formTitle = this.FindControl<TextBlock>("FormTitleText");
                    var nameBox = this.FindControl<TextBox>("NewGameNameTextBox");
                    var repoBox = this.FindControl<TextBox>("NewGameRepoTextBox");
                    var folderBox = this.FindControl<TextBox>("NewGameFolderTextBox");
                    var iconBox = this.FindControl<TextBox>("NewGameIconTextBox");
                    var cancelButton = this.FindControl<Button>("CancelButton");
                    var createEditButton = this.FindControl<Button>("CreateEditButton");

                    if (formTitle != null) formTitle.Text = "Edit App Entry";
                    if (nameBox != null) nameBox.Text = name;
                    if (repoBox != null)
                    {
                        repoBox.Text = repository;
                        repoBox.IsEnabled = false;
                    }
                    if (folderBox != null) folderBox.Text = folderName;
                    if (iconBox != null) iconBox.Text = iconUrl ?? "";
                    if (cancelButton != null) cancelButton.IsVisible = true;
                    if (createEditButton != null) createEditButton.Content = "Update Entry";

                    _editingGameName = name;
                    _editingGameRepository = repository;
                    _editingFolderName = folderName;
                }
                catch (Exception ex)
                {
                    _ = ShowMessageBoxAsync($"Error preparing edit form: {ex.Message}", "Error");
                }
            }
        }

        private string? _editingGameName = null;
        private string? _editingGameRepository = null;
        private string? _editingFolderName = null;

        private async void CancelForm_Click(object sender, RoutedEventArgs e)
        {
            ClearForm();
            SwitchToManageGamesTab(null, null);
        }

        private void ClearForm()
        {
            var nameBox = this.FindControl<TextBox>("NewGameNameTextBox");
            var repoBox = this.FindControl<TextBox>("NewGameRepoTextBox");
            var folderBox = this.FindControl<TextBox>("NewGameFolderTextBox");
            var iconBox = this.FindControl<TextBox>("NewGameIconTextBox");
            var createEditButton = this.FindControl<Button>("CreateEditButton");
            var formTitle = this.FindControl<TextBlock>("FormTitleText");
            var validationStatus = this.FindControl<TextBlock>("ValidationStatusText");

            if (nameBox != null) nameBox.Text = "";
            if (repoBox != null) { repoBox.Text = ""; repoBox.IsEnabled = true; }
            if (folderBox != null) folderBox.Text = "";
            if (iconBox != null) iconBox.Text = "";
            if (createEditButton != null) createEditButton.Content = "Create Entry";
            if (formTitle != null) formTitle.Text = "Create New Entry";
            if (validationStatus != null) validationStatus.Text = "";

            _editingGameName = null;
            _editingGameRepository = null;
            _editingFolderName = null;
        }

        private async void RemoveGameEntry_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is var gameData)
            {
                try
                {
                    var repoProp = gameData.GetType().GetProperty("Repository");
                    var nameProp = gameData.GetType().GetProperty("Name");

                    var repository = repoProp?.GetValue(gameData) as string;
                    var gameName = nameProp?.GetValue(gameData) as string;

                    if (string.IsNullOrEmpty(repository) || string.IsNullOrEmpty(gameName)) return;

                    var confirm = await ShowMessageBoxAsync(
                        $"Are you sure you want to remove '{gameName}' from your apps list?\n\nThis will only remove it from the launcher, not delete your files.",
                        "Confirm Removal",
                        true);

                    if (!confirm) return;

                    var games = await LoadGamesFromJsonAsync();
                    var gameToRemove = games.FirstOrDefault(g => g.Repository == repository);

                    if (gameToRemove != null)
                    {
                        games.Remove(gameToRemove);
                        await SaveGamesToJsonAsync(games);

                        await _gameManager.LoadGamesAsync();
                        ApplySorting();
                        LoadGamesFromJson();

                        _ = ShowMessageBoxAsync($"'{gameName}' was removed successfully.", "Removed");
                    }
                }
                catch (Exception ex)
                {
                    _ = ShowMessageBoxAsync($"Error removing app: {ex.Message}", "Error");
                }
            }
        }

        // App Manager Navigation
        private void NavManageApps_Click(object sender, RoutedEventArgs e)
        {
            SwitchToManageGamesTab(sender, e);
            HeaderTitleText.Text = "Manage Apps";
        }

        private void NavAppBrowser_Click(object sender, RoutedEventArgs e)
        {
            SwitchToAppBrowserTab();
        }

        private void SwitchToAppBrowserTab()
        {
            var manageGamesTab = this.FindControl<ScrollViewer>("ManageGamesTab");
            var createEditTab = this.FindControl<ScrollViewer>("CreateEditTab");
            var appBrowserTab = this.FindControl<ScrollViewer>("AppBrowserTab");

            if (manageGamesTab != null) manageGamesTab.IsVisible = false;
            if (createEditTab != null) createEditTab.IsVisible = false;
            if (appBrowserTab != null) appBrowserTab.IsVisible = true;

            HeaderTitleText.Text = "App Catalog";
            _ = LoadAppCatalogAsync(forceRefresh: false);
        }

        private void EmptyStateOpenAppManager_Click(object sender, RoutedEventArgs e)
        {
            ManageGamesButton_Click(sender, e);
            SwitchToAppBrowserTab();
        }

        private void RefreshAppCatalog_Click(object sender, RoutedEventArgs e)
        {
            _ = LoadAppCatalogAsync(forceRefresh: true);
        }

        // App Catalog
        private static readonly string AppCatalogCachePath = Path.Combine(
            LauncherPaths.DataDirectory, "app_catalog_cache.json");

        private static readonly string AppCatalogVersionPath = Path.Combine(
            LauncherPaths.DataDirectory, "app_catalog_version.txt");

        private async Task LoadAppCatalogAsync(bool forceRefresh)
        {
            var statusText = this.FindControl<TextBlock>("AppCatalogStatusText");
            var versionText = this.FindControl<TextBlock>("AppCatalogVersionText");
            var catalogContent = this.FindControl<StackPanel>("AppCatalogContent");

            if (statusText == null || catalogContent == null)
                return;

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                statusText.Text = "Checking for updates...";
                statusText.IsVisible = true;
                catalogContent.IsVisible = false;
            });

            try
            {
                string repo = _settings?.AppListRepository ?? "SirDiabo/GHLAppList";
                string latestTag = !forceRefresh && File.Exists(AppCatalogCachePath) && File.Exists(AppCatalogVersionPath) &&
                    File.GetLastWriteTimeUtc(AppCatalogCachePath) > DateTime.UtcNow.AddHours(-6)
                    ? (await File.ReadAllTextAsync(AppCatalogVersionPath).ConfigureAwait(false)).Trim()
                    : await FetchLatestCatalogTagAsync(repo).ConfigureAwait(false);

                string cachedVersion = string.Empty;
                if (File.Exists(AppCatalogVersionPath))
                    cachedVersion = (await File.ReadAllTextAsync(AppCatalogVersionPath).ConfigureAwait(false)).Trim();

                bool needsDownload = forceRefresh
                    || !File.Exists(AppCatalogCachePath)
                    || string.IsNullOrEmpty(cachedVersion)
                    || cachedVersion != latestTag;

                if (needsDownload && !string.IsNullOrEmpty(latestTag))
                {
                    try
                    {
                        await Dispatcher.UIThread.InvokeAsync(() =>
                            statusText.Text = $"Downloading catalog {latestTag}...");

                        string catalogJson = await FetchCatalogJsonAsync(repo, latestTag).ConfigureAwait(false);
                        using var validatedCatalog = JsonDocument.Parse(catalogJson);
                        await File.WriteAllTextAsync(AppCatalogCachePath, catalogJson).ConfigureAwait(false);
                        await File.WriteAllTextAsync(AppCatalogVersionPath, latestTag).ConfigureAwait(false);
                        cachedVersion = latestTag;

                        if (_settings != null)
                        {
                            _settings.AppListCachedVersion = latestTag;
                            OnSettingChanged();
                        }
                    }
                    catch (Exception ex) when (ex is System.Net.Http.HttpRequestException or JsonException or TaskCanceledException)
                    {
                        Debug.WriteLine($"Catalog download failed; using cached/bundled catalog: {ex.Message}");
                    }
                }

                var bundledCatalogPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "catalog.json");
                if (!File.Exists(AppCatalogCachePath) && File.Exists(bundledCatalogPath))
                    File.Copy(bundledCatalogPath, AppCatalogCachePath);

                string json = File.Exists(AppCatalogCachePath)
                    ? await File.ReadAllTextAsync(AppCatalogCachePath).ConfigureAwait(false) : "{}";
                var categories = ParseCatalogJson(json);
                if (File.Exists(bundledCatalogPath))
                {
                    var existing = categories.SelectMany(c => c.Entries).Select(e => e.Repository).ToHashSet(StringComparer.OrdinalIgnoreCase);
                    foreach (var category in ParseCatalogJson(await File.ReadAllTextAsync(bundledCatalogPath).ConfigureAwait(false)))
                    {
                        var additions = category.Entries.Where(e => existing.Add(e.Repository)).ToList();
                        if (additions.Count > 0) categories.Add((category.Category, additions));
                    }
                }
                string communityStatus = "";
                if (_settings?.EnableCommunityCatalog != false)
                {
                    await Dispatcher.UIThread.InvokeAsync(() => statusText.Text = "Updating Quiver community catalog…");
                    var community = await CommunityCatalog.LoadAsync(LauncherPaths.DataDirectory, forceRefresh).ConfigureAwait(false);
                    var metadata = community.Entries.ToDictionary(e => e.Repository, StringComparer.OrdinalIgnoreCase);
                    categories = categories.Select(c => (c.Category, c.Entries.Select(e =>
                        metadata.TryGetValue(e.Repository, out var details) ? e with { Details = details } : e).ToList())).ToList();
                    var existing = categories.SelectMany(c => c.Entries).Select(e => e.Repository).ToHashSet(StringComparer.OrdinalIgnoreCase);
                    var additions = community.Entries.Where(e => existing.Add(e.Repository))
                        .Select(e => new CatalogEntry(e.Name, e.Repository, e.FolderName, e.AppIconUrl, "Quiver community catalog", e)).ToList();
                    if (additions.Count > 0) categories.Add(("Quiver community catalog — quiverlauncher.com", additions));
                    communityStatus = community.Status + $" {additions.Count} additional projects.";
                }
                string discoveryStatus = string.Empty;
                if (OperatingSystem.IsMacOS() && forceRefresh)
                    discoveryStatus = await MacReleaseDiscovery.RefreshAsync(categories.SelectMany(c => c.Entries).Select(e => e.Repository),
                        _settings?.GitHubApiToken).ConfigureAwait(false);

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (versionText != null)
                        versionText.Text = string.IsNullOrEmpty(cachedVersion) ? string.Empty : $"Version: {cachedVersion}";

                    statusText.Text = string.Join("\n", new[] { communityStatus, discoveryStatus }.Where(s => !string.IsNullOrEmpty(s)));
                    statusText.IsVisible = !string.IsNullOrEmpty(statusText.Text);
                    catalogContent.IsVisible = true;
                    _loadedCatalogCategories = categories;
                    RenderCatalogCategories(catalogContent, categories);
                });
            }
            catch (Exception ex)
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    statusText.Text = $"Failed to load catalog: {ex.Message}";
                    statusText.IsVisible = true;
                    catalogContent.IsVisible = false;
                });
            }
        }

        private async Task<string> FetchLatestCatalogTagAsync(string repo)
        {
            try
            {
                string url = $"https://api.github.com/repos/{repo}/releases/latest";
                using var client = new System.Net.Http.HttpClient();
                client.DefaultRequestHeaders.Add("User-Agent", "GithubLauncher");
                if (!string.IsNullOrEmpty(_settings?.GitHubApiToken))
                    client.DefaultRequestHeaders.Add("Authorization", $"token {_settings.GitHubApiToken}");

                string response = await client.GetStringAsync(url).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(response);
                if (doc.RootElement.TryGetProperty("tag_name", out var tag))
                    return tag.GetString() ?? string.Empty;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"FetchLatestCatalogTag failed: {ex.Message}");
            }
            return string.Empty;
        }

        private async Task<string> FetchCatalogJsonAsync(string repo, string tag)
        {
            string url = $"https://github.com/{repo}/releases/download/{tag}/apps.json";
            using var client = new System.Net.Http.HttpClient();
            client.DefaultRequestHeaders.Add("User-Agent", "GithubLauncher");
            if (!string.IsNullOrEmpty(_settings?.GitHubApiToken))
                client.DefaultRequestHeaders.Add("Authorization", $"token {_settings.GitHubApiToken}");

            return await client.GetStringAsync(url).ConfigureAwait(false);
        }

        private record CatalogEntry(string Name, string Repository, string FolderName, string AppIconUrl, string Category,
            CommunityCatalogEntry? Details = null);

        private List<(string Category, List<CatalogEntry> Entries)> ParseCatalogJson(string json)
        {
            var result = new List<(string, List<CatalogEntry>)>();
            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (root.ValueKind == JsonValueKind.Object)
                {
                    foreach (var section in root.EnumerateObject())
                    {
                        var entries = new List<CatalogEntry>();
                        if (section.Value.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var item in section.Value.EnumerateArray())
                            {
                                string name = item.TryGetProperty("name", out var n) ? n.GetString() ?? string.Empty : string.Empty;
                                string repo = item.TryGetProperty("repository", out var r) ? r.GetString() ?? string.Empty : string.Empty;
                                string folder = item.TryGetProperty("folderName", out var f) ? f.GetString() ?? string.Empty : string.Empty;
                                string icon = string.Empty;
                                if (item.TryGetProperty("gameIconUrl", out var gi)) icon = gi.GetString() ?? string.Empty;
                                else if (item.TryGetProperty("appIconUrl", out var ai)) icon = ai.GetString() ?? string.Empty;
                                string category = item.TryGetProperty("category", out var c) ? c.GetString() ?? string.Empty : string.Empty;

                                if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(repo))
                                    entries.Add(new CatalogEntry(name, repo, folder, icon, category));
                            }
                        }
                        if (entries.Count > 0)
                            result.Add((section.Name, entries));
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ParseCatalogJson failed: {ex.Message}");
            }
            return result;
        }

        private void RenderCatalogCategories(StackPanel container, List<(string Category, List<CatalogEntry> Entries)> categories)
        {
            _catalogView = null;
            container.Children.Clear();
            var compatibility = MacCompatibility.Load();
            var search = this.FindControl<TextBox>("CatalogSearchBox")?.Text ?? "";
            var descending = this.FindControl<ComboBox>("CatalogSortOrder")?.SelectedIndex == 1;
            var all = categories.SelectMany(c => c.Entries)
                .DistinctBy(e => e.Repository, StringComparer.OrdinalIgnoreCase).ToList();
            var entries = all.Where(e =>
            {
                compatibility.TryGetValue(e.Repository, out var support);
                var systems = CatalogPresentation.Platforms(e.Details?.SupportedOS ?? [], support);
                return CatalogPresentation.MatchesSearch(search, e.Name, e.Repository,
                        e.Details?.DisplayName ?? "", e.Details?.ProjectName ?? "") &&
                    CatalogPresentation.MatchesPlatforms(systems, _catalogSelectedPlatforms, _catalogIncludeUnknown) &&
                    (!OperatingSystem.IsMacOS() || MacCompatibility.MatchesCatalogFilter(support, (MacCatalogFilter)MacCatalogFilterIndex));
            });
            string Title(CatalogEntry entry) => string.IsNullOrWhiteSpace(entry.Details?.DisplayName)
                ? entry.Name : entry.Details.DisplayName;
            var ordered = (descending ? entries.OrderByDescending(Title, StringComparer.OrdinalIgnoreCase)
                : entries.OrderBy(Title, StringComparer.OrdinalIgnoreCase)).ThenBy(e => e.Repository, StringComparer.OrdinalIgnoreCase).ToList();
            var count = new TextBlock
            {
                Text = $"Showing 0 of {ordered.Count} matching projects · {all.Count} total",
                FontSize = 12, Foreground = this.FindResource("ThemeTextSecondary") as IBrush
            };
            container.Children.Add(count);
            if (ordered.Count == 0)
            {
                container.Children.Add(new TextBlock
                {
                    Text = "No projects match. Try another search, platform or Mac compatibility filter.",
                    FontSize = 14, TextWrapping = TextWrapping.Wrap,
                    Foreground = this.FindResource("ThemeTextSecondary") as IBrush,
                    Margin = new Thickness(0, 24)
                });
                return;
            }
            var libraryRepos = (_gameManager?.Games.Select(g => g.Repository ?? "") ?? [])
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var cards = new WrapPanel { VerticalAlignment = VerticalAlignment.Top };
            container.Children.Add(cards);
            var footer = new TextBlock
            {
                FontSize = 12, Foreground = this.FindResource("ThemeTextSecondary") as IBrush,
                HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 20)
            };
            container.Children.Add(footer);
            _catalogView = new(ordered, compatibility, libraryRepos, cards, count, footer, all.Count);
            AppendCatalogCards(_catalogView);
        }

        private Border BuildCatalogCard(CatalogEntry entry, bool alreadyAdded, MacCompatibilityEntry? compatibility)
        {
            var textBrush = this.FindResource("ThemeText") as IBrush;
            var secondaryBrush = this.FindResource("ThemeTextSecondary") as IBrush;
            var coverUrl = entry.Details?.CoverUrl;
            var hasCover = !string.IsNullOrWhiteSpace(coverUrl);
            var artwork = hasCover ? coverUrl! : entry.AppIconUrl;
            var cover = new Grid
            {
                Height = 132, Background = this.FindResource("ThemeLighter") as IBrush,
                ClipToBounds = true
            };
            cover.Children.Add(new TextBlock
            {
                Text = entry.Name[..1].ToUpperInvariant(), FontSize = 44, FontWeight = FontWeight.Bold,
                Foreground = secondaryBrush, Opacity = 0.25,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
            });
            if (!string.IsNullOrEmpty(artwork))
            {
                var image = new Image
                {
                    Stretch = hasCover ? Stretch.UniformToFill : Stretch.Uniform,
                    Margin = hasCover ? new Thickness(0) : new Thickness(20),
                    HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch
                };
                ImageLoader.SetSource(image, artwork);
                cover.Children.Add(image);
            }
            if (alreadyAdded)
            {
                cover.Children.Add(new Border
                {
                    Background = new SolidColorBrush(Color.Parse("#D8E5F0")), CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(7, 3), Margin = new Thickness(10),
                    HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
                    Child = new TextBlock { Text = "IN LIBRARY", FontSize = 9, FontWeight = FontWeight.Bold,
                        Foreground = new SolidColorBrush(Color.Parse("#172331")) }
                });
            }

            var body = new StackPanel { Margin = new Thickness(14, 12, 14, 8), Spacing = 5 };
            body.Children.Add(new TextBlock
            {
                Text = CatalogPresentation.ProjectLabel(entry.Details?.ProjectType ?? ""),
                FontSize = 10, LetterSpacing = 1, Foreground = secondaryBrush
            });
            var title = new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(entry.Details?.DisplayName) ? entry.Name : entry.Details.DisplayName,
                FontSize = 15, FontWeight = FontWeight.SemiBold, Foreground = textBrush,
                TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis,
                MaxLines = 2, Height = 40
            };
            ToolTip.SetTip(title, entry.Name);
            body.Children.Add(title);
            var project = new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(entry.Details?.ProjectName) ? entry.Repository : entry.Details.ProjectName,
                FontSize = 11, Foreground = secondaryBrush, TextTrimming = TextTrimming.CharacterEllipsis
            };
            ToolTip.SetTip(project, entry.Repository + "\n" + entry.Category);
            body.Children.Add(project);
            var platforms = new WrapPanel { MinHeight = 44, Margin = new Thickness(0, 4, 0, 0) };
            var systems = CatalogPresentation.Platforms(entry.Details?.SupportedOS ?? [], compatibility);
            foreach (var system in systems.Length == 0 ? new[] { "unknown" } : systems)
            {
                var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
                var icon = system switch { "macos" => "mac", "windows" => "win", "linux" => "lin", _ => null };
                if (icon != null)
                {
                    var image = new Image { Width = 12, Height = 12 };
                    ImageLoader.SetSource(image, $"avares://GithubLauncher/Assets/Icons/platform_{icon}.png");
                    content.Children.Add(image);
                }
                content.Children.Add(new TextBlock
                {
                    Text = system == "unknown" ? "Platforms not listed" : CatalogPresentation.PlatformLabel(system),
                    FontSize = 10, Foreground = textBrush
                });
                var badge = new Border
                {
                    Child = content, CornerRadius = new CornerRadius(4), Padding = new Thickness(6, 3),
                    Background = this.FindResource("ThemeLighter") as IBrush,
                    Margin = new Thickness(0, 0, 5, 5), VerticalAlignment = VerticalAlignment.Top
                };
                ToolTip.SetTip(badge, "Platforms listed by the project catalog or recorded release assets. This is not a Mac gameplay report.");
                platforms.Children.Add(badge);
            }
            body.Children.Add(platforms);
            if (OperatingSystem.IsMacOS())
            {
                var label = MacCompatibility.BrowseCategory(compatibility) switch
                {
                    MacCatalogFilter.Native => "Native Mac release",
                    MacCatalogFilter.WindowsPlayable => "Windows · playable on this Mac",
                    _ => MacCompatibility.CatalogCategory(compatibility) == "candidate"
                        ? "Windows · untested on Mac" : "Mac support unconfirmed"
                };
                var support = new TextBlock
                {
                    Text = label + "\n" + MacGameplay.Summary(compatibility), FontSize = 11,
                    TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis,
                    MaxLines = 3, Foreground = secondaryBrush
                };
                ToolTip.SetTip(support, compatibility == null ? "No Mac release or gameplay evidence recorded." :
                    $"{compatibility.Release}: {compatibility.AssetName}\n{compatibility.Requirements}\n{compatibility.Evidence}\n\n{MacGameplay.Details(compatibility)}");
                body.Children.Add(support);
            }

            var actions = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(14, 8, 14, 14) };
            var detailsButton = new Button
            {
                Content = "Setup guide", FontSize = 11, HorizontalAlignment = HorizontalAlignment.Left,
                Padding = new Thickness(8, 6), MinHeight = 32
            };
            detailsButton.Click += async (_, _) => await ShowGameDataRequirementsAsync(entry.Name, entry.Repository);
            ToolTip.SetTip(detailsButton, GameDataRequirements.Find(entry.Repository).Summary);
            actions.Children.Add(detailsButton);
            var add = new Button
            {
                Content = alreadyAdded ? "Added" : "+ Add", IsEnabled = !alreadyAdded,
                FontSize = 11, MinHeight = 32, Padding = new Thickness(12, 6), Tag = entry,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            add.Classes.Add("options");
            if (!alreadyAdded) add.Click += CatalogAddEntry_Click;
            Grid.SetColumn(add, 1);
            actions.Children.Add(add);
            var layout = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto") };
            layout.Children.Add(cover);
            Grid.SetRow(body, 1);
            layout.Children.Add(body);
            Grid.SetRow(actions, 2);
            layout.Children.Add(actions);
            return new Border
            {
                Width = 236, Height = 412, Margin = new Thickness(0, 0, 14, 14),
                Background = this.FindResource("ThemeBase") as IBrush,
                BorderBrush = this.FindResource("ThemeBorder") as IBrush, BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10), ClipToBounds = true, Child = layout
            };
        }

        private async Task<bool> ConfirmWindowsOnlyAdditionAsync(string? name)
        {
            var dialog = new Window
            {
                Title = "No native macOS release", Width = 520, MaxHeight = 600,
                SizeToContent = SizeToContent.Height, CanResize = false,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };
            var panel = new StackPanel { Margin = new Thickness(24), Spacing = 16 };
            panel.Children.Add(new TextBlock { Text = $"{name} has no native macOS download.",
                FontSize = 18, FontWeight = FontWeight.Bold, TextWrapping = TextWrapping.Wrap });
            panel.Children.Add(new TextBlock { Text = "A Windows build is available. Adding it enables experimental Windows downloads. You will need Steam / NotProton or CrossOver, and gameplay on your Mac is not confirmed. Configure the runner before downloading.", TextWrapping = TextWrapping.Wrap });
            var buttons = new WrapPanel();
            var cancel = new Button { Content = "Cancel", Margin = new Thickness(0, 0, 12, 0) };
            var add = new Button { Content = "Add Windows build (experimental)" };
            cancel.Click += (_, _) => dialog.Close(false);
            add.Click += (_, _) => dialog.Close(true);
            buttons.Children.Add(cancel);
            buttons.Children.Add(add);
            panel.Children.Add(buttons);
            dialog.Content = new ScrollViewer { Content = panel };
            return await dialog.ShowDialog<bool>(this);
        }

        private async void CatalogAddEntry_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.Tag is not CatalogEntry entry)
                return;

            button.IsEnabled = false;
            try
            {
                var apps = await _gameManager.GetAppsAsync();
                if (apps.Any(a => string.Equals(a.Repository, entry.Repository, StringComparison.OrdinalIgnoreCase)))
                {
                    await ShowMessageBoxAsync($"{entry.Name} is already in your app list.", "Already Added");
                    return;
                }

                var newApp = new GameInfo
                {
                    Name = entry.Name,
                    Repository = entry.Repository,
                    FolderName = entry.FolderName,
                    GameIconUrl = entry.AppIconUrl,
                    IsCustom = true,
                    GameManager = _gameManager,
                };
                bool windowsOnly = false;
                if (OperatingSystem.IsMacOS())
                {
                    // Check published downloads before saving anything to the library.
                    var releases = await newApp.FetchPublishedReleasesAsync(_gameManager.HttpClient);
                    var available = MacCompatibility.AvailablePlatforms(releases);
                    if (!available.Native)
                    {
                        if (!available.Windows)
                        {
                            await ShowMessageBoxAsync($"{entry.Name} has no supported native macOS or Windows download available. The game was not added.", "No supported download");
                            return;
                        }
                        if (!await ConfirmWindowsOnlyAdditionAsync(entry.Name)) return;
                        windowsOnly = true;
                        newApp.PreferWindowsBuild = true;
                        newApp.AllowCrossOverTesting = true;
                    }
                }

                apps.Add(newApp);
                await _gameManager.SaveAppsAsync(apps);
                await _gameManager.LoadGamesAsync();
                button.Content = "Added";
                button.Click -= CatalogAddEntry_Click;
                UpdateEmptyStateVisibility();
                if (windowsOnly)
                    await ConfigureMacLaunchAsync(_gameManager.Games.FirstOrDefault(g =>
                        string.Equals(g.Repository, entry.Repository, StringComparison.OrdinalIgnoreCase)) ?? newApp);
            }
            catch (Exception ex)
            {
                await ShowMessageBoxAsync($"Could not add or configure {entry.Name}: {ex.Message}", "Add game error");
            }
            finally
            {
                button.IsEnabled = !Equals(button.Content, "Added");
            }
        }

        private void UpdateEmptyStateVisibility()
        {
            var overlay = this.FindControl<Border>("EmptyStateOverlay");
            if (overlay == null)
                return;

            bool isEmpty = _gameManager?.Games == null || _gameManager.Games.Count == 0;
            overlay.IsVisible = isEmpty;
        }

                private void CloseManageGames_Click(object sender, RoutedEventArgs e)
        {
            _isGamesManagerOpen = false;

            // Hide Manage Apps panel
            var manageGamesPanel = this.FindControl<Border>("ManageGamesPanel");
            if (manageGamesPanel != null)
            {
                manageGamesPanel.IsVisible = false;
            }

            // Show main content
            HeaderTitleText.Text = "Library";
        }

        private void MusicVolumeSlider_ValueChanged(object sender, Avalonia.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            if (sender is Slider slider)
            {
                MusicVolume = (float)slider.Value;
                _settings.MusicVolume = MusicVolume;
                AppSettings.Save(_settings);
            }
        }

        private void PlayLauncherMusic(string path)
        {
            try
            {
                if (!File.Exists(path))
                    return;

                StopLauncherMusic();

                // Use runtime detection
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                    PlayMusicWindows(path);
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                    PlayMusicLinux(path);
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                    PlayMusicMac(path);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to play launcher music: {ex.Message}");
            }
        }

        private void PlayMusicWindows(string path)
        {
            #if WINDOWS
            try
            {
                _audioFileReader = new AudioFileReader(path);
                _audioFileReader.Volume = MusicVolume;

                _waveOut = new WaveOutEvent();
                _waveOut.Init(_audioFileReader);

                // Enable looping
                _waveOut.PlaybackStopped += (sender, args) =>
                {
                    if (_audioFileReader != null && _waveOut != null)
                    {
                        _audioFileReader.Position = 0;
                        _waveOut.Play();
                    }
                };

                _waveOut.Play();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"NAudio playback failed: {ex.Message}");
            }
        #else
            Debug.WriteLine("Windows audio playback not available on this platform");
        #endif
        }

        private void PlayMusicLinux(string path)
        {
            string[] players = { "ffplay", "mpv", "cvlc", "mplayer" };

            foreach (var player in players)
            {
                try
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = player,
                        Arguments = player switch
                        {
                            "ffplay" => $"-nodisp -autoexit -loop 0 -volume {(int)(MusicVolume * 100)} \"{path}\"",
                            "mpv" => $"--no-video --loop=inf --volume={MusicVolume * 100} \"{path}\"",
                            "cvlc" => $"--no-video --loop --volume {(int)(MusicVolume * 512)} \"{path}\"",
                            "mplayer" => $"-loop 0 -volume {(int)(MusicVolume * 100)} \"{path}\"",
                            _ => $"\"{path}\""
                        },
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    };

                    _musicProcess = Process.Start(psi);
                    if (_musicProcess != null)
                    {
                        _musicProcess.EnableRaisingEvents = true;
                        Debug.WriteLine($"Playing music with {player}");
                        return;
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Failed to start {player}: {ex.Message}");
                    continue;
                }
            }

            Debug.WriteLine("No suitable audio player found on Linux. Install one of: ffplay, mpv, vlc, mplayer");
        }

        private void PlayMusicMac(string path)
        {
            try
            {
                // afplay volume is 0-255 (0-1 range needs to be converted)
                var volumeValue = MusicVolume * 255f;

                var psi = new ProcessStartInfo
                {
                    FileName = "afplay",
                    Arguments = $"-v {volumeValue} \"{path}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                _musicProcess = Process.Start(psi);

                if (_musicProcess != null)
                {
                    _musicProcess.EnableRaisingEvents = true;
                    _musicProcess.Exited += (s, e) =>
                    {
                        if (!string.IsNullOrEmpty(LauncherMusicPath) && File.Exists(LauncherMusicPath))
                        {
                            try
                            {
                                Dispatcher.UIThread.Post(() =>
                                {
                                    if (!string.IsNullOrEmpty(LauncherMusicPath))
                                    {
                                        PlayMusicMac(LauncherMusicPath);
                                    }
                                });
                            }
                            catch (Exception ex)
                            {
                                Debug.WriteLine($"Failed to restart music: {ex.Message}");
                            }
                        }
                    };

                    Debug.WriteLine($"Playing music with afplay at volume {volumeValue}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"afplay failed: {ex.Message}");
            }
        }

        private void StopLauncherMusic()
        {
            try
            {
                #if WINDOWS
                if (_waveOut != null)
                {
                    _waveOut.Stop();
                    _waveOut.Dispose();
                    _waveOut = null;
                }

                if (_audioFileReader != null)
                {
                    _audioFileReader.Dispose();
                    _audioFileReader = null;
                }
                #endif

                if (_musicProcess != null)
                {
                    try
                    {
                        if (!_musicProcess.HasExited)
                        {
                            _musicProcess.Kill();
                        }
                    }
                    catch (InvalidOperationException)
                    {
                        // Process already exited, ignore
                    }

                    _musicProcess.Dispose();
                    _musicProcess = null;
                }

                _musicPausedByDeactivation = false;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to stop launcher music: {ex.Message}");
            }
        }

        private async Task FadeMusicAsync(float targetVolume, int durationMs)
        {
        #if WINDOWS
            if (_audioFileReader == null)
                return;

        // Cancel any ongoing fade
        _fadeTaskCts?.Cancel();
        _fadeTaskCts = new System.Threading.CancellationTokenSource();
        var token = _fadeTaskCts.Token;

        try
        {
            float currentVolume = _audioFileReader.Volume;
            float targetVol = targetVolume;

            if (Math.Abs(currentVolume - targetVol) < 0.001f)
                return;

            int steps = 20;
            int stepDelay = durationMs / steps;
            float volumeStep = (targetVol - currentVolume) / steps;

            for (int i = 0; i < steps; i++)
            {
                if (token.IsCancellationRequested || _audioFileReader == null)
                    return;

                currentVolume += volumeStep;
                _audioFileReader.Volume = Math.Clamp(currentVolume, 0f, 1f);

                await Task.Delay(stepDelay, token);
            }

                if (_audioFileReader != null && !token.IsCancellationRequested)
                {
                    _audioFileReader.Volume = targetVol;
                }
            }
            catch (OperationCanceledException)
            {
                // Fade was cancelled
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error during music fade: {ex.Message}");
            }
            #else
                if (targetVolume < 0.01f)
                {
                    if (_musicProcess != null && !_musicProcess.HasExited)
                    {
                        try
                        {
                            _musicProcess.Kill();
                            Debug.WriteLine("Music paused (process killed)");
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Failed to pause music: {ex.Message}");
                        }
                    }
                }
                else if (targetVolume > 0.01f)
                {
                    if (_musicProcess == null || _musicProcess.HasExited)
                    {
                        if (!string.IsNullOrEmpty(LauncherMusicPath) && File.Exists(LauncherMusicPath))
                        {
                            PlayLauncherMusic(LauncherMusicPath);
                            Debug.WriteLine("Music resumed");
                        }
                    }
                }
    
                await Task.CompletedTask;
            #endif
        }

        private void MainWindow_KeyDown(object? sender, KeyEventArgs e)
        {
            if (_isProcessingInput || !IsActive)
                return;

            _isProcessingInput = true;

            try
            {
                switch (e.Key)
                {
                    case Key.Space:
                        // Check if a text input control has focus
                        var focusedElement = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement();
                        if (focusedElement is TextBox)
                        {
                            // Let space pass through to text input controls
                        }
                        else
                        {
                            // Handle space as confirm action for other controls
                            HandleConfirmAction();
                            e.Handled = true;
                        }
                        break;

                    case Key.LeftShift:
                        HandleCancelAction();
                        e.Handled = true;
                        break;

                    case Key.Up:
                        _inputService?.HandleNavigation(Services.NavigationDirection.Up);
                        e.Handled = true;
                        break;

                    case Key.Down:
                        _inputService?.HandleNavigation(Services.NavigationDirection.Down);
                        e.Handled = true;
                        break;

                    case Key.Left:
                        _inputService?.HandleNavigation(Services.NavigationDirection.Left);
                        e.Handled = true;
                        break;

                    case Key.Right:
                        _inputService?.HandleNavigation(Services.NavigationDirection.Right);
                        e.Handled = true;
                        break;
                }
            }
            finally
            {
                _isProcessingInput = false;
            }
        }

        private void MainWindow_KeyUp(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.Up || e.Key == Key.Down || e.Key == Key.Left || e.Key == Key.Right)
            {
                _inputService?.ResetNavigationTimer();
            }
        }

        private void HandleConfirmAction()
        {
            var focused = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement();

            if (focused is MenuItem menuItem)
            {
                // Trigger the menu item click
                menuItem.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            }
            else if (focused is Button button)
            {
                // If button has a context menu, open it
                if (button.ContextMenu != null)
                {
                    button.ContextMenu.PlacementTarget = button;
                    button.ContextMenu.Placement = PlacementMode.Bottom;
                    button.ContextMenu.Open();

                    // Focus first menu item after opening
                    Dispatcher.UIThread.Post(() =>
                    {
                        var firstMenuItem = button.ContextMenu.Items.OfType<MenuItem>().FirstOrDefault();
                        firstMenuItem?.Focus();
                    }, DispatcherPriority.Loaded);
                }
                else
                {
                    // Normal button click
                    button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                }
            }
            else if (focused is CheckBox checkBox)
            {
                checkBox.IsChecked = !checkBox.IsChecked;
            }
            else if (focused is ToggleButton toggleButton)
            {
                toggleButton.IsChecked = !toggleButton.IsChecked;
            }
        }

        private void HandleCancelAction()
        {
            // First check if any context menu is open and close it
            var allButtons = this.GetVisualDescendants().OfType<Button>();
            foreach (var button in allButtons)
            {
                if (button.ContextMenu?.IsOpen == true)
                {
                    button.ContextMenu.Close();
                    button.Focus(); // Return focus to the button
                    return;
                }
            }

            // Close settings panel if open
            if (isSettingsPanelOpen && SettingsPanel != null)
            {
                isSettingsPanelOpen = false;
                SettingsPanel.IsVisible = false;

                // Return focus to settings button
                var settingsButton = this.FindControl<Button>("SettingsButton");
                if (settingsButton != null)
                {
                    settingsButton.Focus();
                }
                else
                {
                    // Fallback: focus the first focusable element outside settings panel
                    var firstFocusable = this.GetVisualDescendants()
                        .OfType<Control>()
                        .FirstOrDefault(c => c.IsVisible && c.IsEnabled && c.Focusable && !IsInsideSettingsPanel(c));
                    firstFocusable?.Focus();
                }
                return;
            }
            // Close changelog if open
            if (_isChangelogOpen)
            {
                CloseChangelog();
                return;
            }
        }

        private bool IsInsideSettingsPanel(Control control)
        {
            var parent = control.Parent;
            while (parent != null)
            {
                if (parent == SettingsPanel)
                    return true;
                parent = parent.Parent;
            }
            return false;
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);

            // Unsubscribe from events
            this.Activated -= MainWindow_Activated;
            this.Deactivated -= MainWindow_Deactivated;

            // Stop Launcher Music
            _fadeTaskCts?.Cancel();
            _fadeTaskCts?.Dispose();
            StopLauncherMusic();

            if (_inputService != null)
            {
                _inputService.OnConfirm -= HandleConfirmAction;
                _inputService.OnCancel -= HandleCancelAction;
                _inputService.Dispose();
            }
        }

        protected override void OnClosing(WindowClosingEventArgs e)
        {
            _fadeTaskCts?.Cancel();
            StopLauncherMusic();
            base.OnClosing(e);
        }

        private void MainWindow_Activated(object? sender, EventArgs e)
        {
            if (!_trackingLaunchedGameProcess)
            {
                _launchedGameOwnsInput = false;
            }

            _inputService?.SetWindowActive(true);
            if (_settings.EnableGamepadInput && !_launchedGameOwnsInput)
            {
                _inputService?.SetGamepadEnabled(true);
            }

            #if WINDOWS
                        _ = FadeMusicAsync(MusicVolume, FADE_DURATION_MS);
            #else
                if (_musicPausedByDeactivation)
                {
                    _musicPausedByDeactivation = false;
                    _ = FadeMusicAsync(MusicVolume, FADE_DURATION_MS);
                }
            #endif
        }

        private void MainWindow_Deactivated(object? sender, EventArgs e)
        {
            _inputService?.SetWindowActive(false);
            _inputService?.SetGamepadEnabled(false);

            #if WINDOWS
                        _ = FadeMusicAsync(0f, FADE_DURATION_MS);
            #else
                if (_musicProcess != null && !_musicProcess.HasExited)
                {
                    _musicPausedByDeactivation = true;
                    _ = FadeMusicAsync(0f, FADE_DURATION_MS);
                }
            #endif
        }

        private void SubscribeToGameEvents(GameInfo game)
        {
            // Unsubscribe
            game.GameProcessStarted -= OnGameProcessStarted;
            game.GameProcessStarted += OnGameProcessStarted;
            game.MacLaunchSetupRequested = async (selectedGame, notice) =>
                await Dispatcher.UIThread.InvokeAsync(async () => await ConfigureMacLaunchAsync(selectedGame, notice));
        }

        private void OnGameProcessStarted(Process? process)
        {
            _launchedGameOwnsInput = true;
            _trackingLaunchedGameProcess = process != null;
            _inputService?.SetWindowActive(false);
            _inputService?.SetGamepadEnabled(false);

            if (process == null)
            {
                return;
            }

            _ = Task.Run(async () =>
            {
                try
                {
                    await process.WaitForExitAsync();

                    if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                    {
                        var currentProcessGroupId = await TryGetProcessGroupIdAsync(Environment.ProcessId);
                        var launchedProcessGroupId = await TryGetProcessGroupIdAsync(process.Id);
                        if (launchedProcessGroupId.HasValue && launchedProcessGroupId != currentProcessGroupId)
                        {
                            while (await HasActiveProcessGroupAsync(launchedProcessGroupId.Value))
                            {
                                await Task.Delay(1000);
                            }
                        }
                    }
                }
                catch { /* process may have already exited */ }

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    _trackingLaunchedGameProcess = false;
                    _launchedGameOwnsInput = false;

                    if (IsActive && _settings.EnableGamepadInput)
                    {
                        _inputService?.SetGamepadEnabled(true);
                        _inputService?.SetWindowActive(true);
                    }
                });
            });
        }

        private static async Task<int?> TryGetProcessGroupIdAsync(int processId)
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = "ps",
                    Arguments = $"-o pgid= -p {processId}",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using var psProcess = Process.Start(startInfo);
                if (psProcess == null)
                {
                    return null;
                }

                var output = await psProcess.StandardOutput.ReadToEndAsync();
                await psProcess.WaitForExitAsync();

                if (psProcess.ExitCode != 0)
                {
                    return null;
                }

                var trimmed = output.Trim();
                if (int.TryParse(trimmed, out var processGroupId))
                {
                    return processGroupId;
                }
            }
            catch
            {
            }

            return null;
        }

        private static async Task<bool> HasActiveProcessGroupAsync(int processGroupId)
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = "ps",
                    Arguments = $"-o pid= -g {processGroupId}",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using var psProcess = Process.Start(startInfo);
                if (psProcess == null)
                {
                    return false;
                }

                var output = await psProcess.StandardOutput.ReadToEndAsync();
                await psProcess.WaitForExitAsync();

                if (psProcess.ExitCode != 0)
                {
                    return false;
                }

                return output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                    .Any(line => !string.IsNullOrWhiteSpace(line));
            }
            catch
            {
                return false;
            }
        }

        private async void ShowChangelog_Click(object sender, RoutedEventArgs e)
        {
            var menuItem = sender as MenuItem;
            var game = menuItem?.CommandParameter as GameInfo;

            if (game == null || string.IsNullOrEmpty(game.Repository))
            {
                await ShowMessageBoxAsync("Unable to retrieve changelog information.", "Error");
                return;
            }

            _currentChangelogGame = game;
            await ShowChangelogAsync(game);
        }

        private async Task ShowChangelogAsync(GameInfo game)
        {
            try
            {
                _isChangelogOpen = true;

                // Show changelog panel
                var changelogPanel = this.FindControl<Border>("ChangelogPanel");
                if (changelogPanel != null)
                {
                    changelogPanel.IsVisible = true;
                }

                // Update header title
                var headerTitle = this.FindControl<TextBlock>("HeaderTitleText");
                if (headerTitle != null)
                {
                    headerTitle.Text = $"{game.Name} - Version {game.LatestVersion ?? "Unknown"}";
                }

                // Update changelog title
                var changelogTitle = this.FindControl<TextBlock>("ChangelogTitle");
                if (changelogTitle != null)
                {
                    changelogTitle.Text = $"{game.Name} Changelog";
                }

                // Fetch and render changelog
                var changelogContent = this.FindControl<ItemsControl>("ChangelogContent");
                if (changelogContent != null)
                {
                    // Show loading placeholder
                    var loadingPanel = new StackPanel();
                    loadingPanel.Children.Add(new TextBlock
                    {
                        Text = "Loading changelog...",
                        Foreground = new SolidColorBrush(Color.Parse("#B8B8B8")),
                        FontSize = 14
                    });
                    changelogContent.ItemsSource = new[] { loadingPanel };
                }

                string changelogText = await FetchChangelogAsync(game.Repository);

                if (changelogContent != null)
                {
                    changelogContent.ItemsSource = ParseMarkdown(changelogText);
                }
            }
            catch (Exception ex)
            {
                await ShowMessageBoxAsync($"Failed to load changelog: {ex.Message}", "Error");
                CloseChangelog();
            }
        }

        private List<Control> ParseMarkdown(string markdown)
        {
            var controls = new List<Control>();
            if (string.IsNullOrWhiteSpace(markdown))
            {
                controls.Add(new SelectableTextBlock
                {
                    Text = "No changelog available.",
                    Foreground = new SolidColorBrush(Color.Parse("#B8B8B8")),
                    FontSize = 14
                });
                return controls;
            }

            var lines = markdown.Split('\n');
            var listItems = new List<string>();
            var codeBlockLines = new List<string>();
            bool inCodeBlock = false;

            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i].TrimEnd('\r');

                // Code blocks
                if (line.TrimStart().StartsWith("```"))
                {
                    if (inCodeBlock)
                    {
                        if (codeBlockLines.Count > 0)
                        {
                            var codeBlock = new Border
                            {
                                Background = new SolidColorBrush(Color.Parse("#1e1e1e")),
                                BorderBrush = new SolidColorBrush(Color.Parse("#2d2d30")),
                                BorderThickness = new Thickness(1),
                                CornerRadius = new CornerRadius(4),
                                Padding = new Thickness(12),
                                Margin = new Thickness(0, 8, 0, 8)
                            };
                            codeBlock.Child = new SelectableTextBlock
                            {
                                Text = string.Join("\n", codeBlockLines),
                                FontFamily = new FontFamily("Consolas,Courier New,monospace"),
                                FontSize = 13,
                                Foreground = new SolidColorBrush(Color.Parse("#d4d4d4"))
                            };
                            controls.Add(codeBlock);
                            codeBlockLines.Clear();
                        }
                        inCodeBlock = false;
                    }
                    else
                    {
                        FlushListItems(controls, listItems);
                        inCodeBlock = true;
                    }
                    continue;
                }

                if (inCodeBlock)
                {
                    codeBlockLines.Add(line);
                    continue;
                }

                // GitHub alerts: > [!NOTE], etc.
                var alertMatch = System.Text.RegularExpressions.Regex.Match(line.TrimStart(),
                    @"^>\s*\[!(NOTE|TIP|IMPORTANT|WARNING|CAUTION)\]",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);

                if (alertMatch.Success)
                {
                    FlushListItems(controls, listItems);

                    var alertType = alertMatch.Groups[1].Value.ToUpper();
                    var alertContentLines = new List<string>();

                    // Move to the first line of content
                    i++;

                    // Collect all lines belonging to the alert block
                    while (i < lines.Length)
                    {
                        var nextLine = lines[i].TrimEnd('\r');

                        // Stop if the line is empty
                        if (string.IsNullOrWhiteSpace(nextLine))
                        {
                            break;
                        }

                        var trimmedLine = nextLine.TrimStart();

                        if (trimmedLine.StartsWith(">"))
                        {
                            // Standard blockquote line: strip the '>'
                            var content = trimmedLine.Substring(1);
                            if (content.StartsWith(" ")) content = content.Substring(1);
                            alertContentLines.Add(content);
                        }
                        else
                        {
                            // Lazy continuation
                            alertContentLines.Add(trimmedLine);
                        }
                        i++;
                    }

                    // Define colors and icon names
                    var (borderColorHex, iconPath) = alertType switch
                    {
                        "NOTE" => ("#0969da", "markdown_info.png"),
                        "TIP" => ("#1a7f37", "markdown_tip.png"),
                        "IMPORTANT" => ("#8250df", "markdown_important.png"),
                        "WARNING" => ("#9a6700", "markdown_warning.png"),
                        "CAUTION" => ("#d1242f", "markdown_caution.png"),
                        _ => ("#2d2d30", "markdown_info.png")
                    };

                    var alertColor = Color.Parse(borderColorHex);
                    var alertBrush = new SolidColorBrush(alertColor);

                    var alertBorder = new Border
                    {
                        BorderBrush = alertBrush,
                        BorderThickness = new Thickness(4, 0, 0, 0),
                        CornerRadius = new CornerRadius(4),
                        Padding = new Thickness(16, 12, 16, 12),
                        Margin = new Thickness(0, 8, 0, 8),
                        Background = new SolidColorBrush(alertColor) { Opacity = 0.05 }
                    };

                    var alertPanel = new StackPanel();

                    // Title row with icon tinted to border color
                    var titlePanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };

                    try
                    {
                        // Use a Rectangle + OpacityMask to draw the icon in the alert's color
                        var iconRect = new Avalonia.Controls.Shapes.Rectangle
                        {
                            Width = 16,
                            Height = 16,
                            Margin = new Thickness(0, 0, 8, 0),
                            Fill = alertBrush,
                            VerticalAlignment = VerticalAlignment.Center,
                            OpacityMask = new ImageBrush
                            {
                                Source = new Avalonia.Media.Imaging.Bitmap(
                                    Avalonia.Platform.AssetLoader.Open(
                                        new Uri($"avares://GithubLauncher/Assets/{iconPath}")))
                            }
                        };
                        titlePanel.Children.Add(iconRect);
                    }
                    catch (Exception)
                    {
                        // Fallback circle if icon load fails
                        titlePanel.Children.Add(new Avalonia.Controls.Shapes.Ellipse
                        {
                            Width = 8,
                            Height = 8,
                            Fill = alertBrush,
                            Margin = new Thickness(0, 0, 8, 0)
                        });
                    }

                    titlePanel.Children.Add(new SelectableTextBlock
                    {
                        Text = alertType,
                        FontSize = 14,
                        FontWeight = FontWeight.Bold,
                        Foreground = alertBrush,
                        VerticalAlignment = VerticalAlignment.Center
                    });

                    alertPanel.Children.Add(titlePanel);

                    // Parse the inner content for markdown (bold, links, etc.)
                    var contentText = string.Join("\n", alertContentLines);
                    var contentBlocks = ParseInlineMarkdown(contentText);
                    foreach (var block in contentBlocks)
                    {
                        block.Margin = new Thickness(0, 2, 0, 2);
                        alertPanel.Children.Add(block);
                    }

                    alertBorder.Child = alertPanel;
                    controls.Add(alertBorder);
                    continue;
                }

                // Headers
                if (line.StartsWith("#"))
                {
                    FlushListItems(controls, listItems);
                    int level = 0;
                    while (level < line.Length && line[level] == '#') level++;
                    var headerText = line.Substring(level).Trim();
                    var fontSize = level switch { 1 => 24, 2 => 20, 3 => 18, 4 => 16, _ => 14 };
                    var fontWeight = level <= 2 ? FontWeight.Bold : FontWeight.SemiBold;

                    controls.Add(new SelectableTextBlock
                    {
                        Text = headerText,
                        FontSize = fontSize,
                        FontWeight = fontWeight,
                        Foreground = new SolidColorBrush(Colors.White),
                        Margin = new Thickness(0, level == 1 ? 16 : 12, 0, 8)
                    });
                    continue;
                }

                // Lists
                if (line.TrimStart().StartsWith("* ") || line.TrimStart().StartsWith("- "))
                {
                    var itemText = line.TrimStart().Substring(2);
                    listItems.Add("• " + itemText);
                    continue;
                }

                var orderedMatch = System.Text.RegularExpressions.Regex.Match(line.TrimStart(), @"^(\d+)\.\s+(.+)");
                if (orderedMatch.Success)
                {
                    listItems.Add(orderedMatch.Groups[1].Value + ". " + orderedMatch.Groups[2].Value);
                    continue;
                }

                if (string.IsNullOrWhiteSpace(line) || line.Trim().StartsWith("---"))
                {
                    FlushListItems(controls, listItems);
                    if (line.Trim().StartsWith("---"))
                        controls.Add(new Border { Height = 1, Background = new SolidColorBrush(Color.Parse("#2d2d30")), Margin = new Thickness(0, 12, 0, 12) });
                    continue;
                }

                FlushListItems(controls, listItems);
                if (!string.IsNullOrWhiteSpace(line))
                {
                    var blocks = ParseInlineMarkdown(line);
                    foreach (var block in blocks)
                    {
                        block.Margin = new Thickness(0, 0, 0, 8);
                        controls.Add(block);
                    }
                }
            }

            FlushListItems(controls, listItems);
            return controls;
        }

        private List<Control> ParseInlineMarkdown(string text)
        {
            var blocks = new List<Control>();
            var panel = new WrapPanel { Orientation = Orientation.Horizontal };

            int i = 0;
            var currentText = new StringBuilder();

            void FlushText()
            {
                if (currentText.Length > 0)
                {
                    panel.Children.Add(new SelectableTextBlock
                    {
                        Text = currentText.ToString(),
                        FontSize = 14,
                        Foreground = new SolidColorBrush(Color.Parse("#B8B8B8")),
                        VerticalAlignment = VerticalAlignment.Center,
                        TextWrapping = TextWrapping.Wrap
                    });
                    currentText.Clear();
                }
            }

            void AddLineBreak()
            {
                // Add all current panel content to blocks
                if (panel.Children.Count > 0)
                {
                    blocks.Add(panel);
                    panel = new WrapPanel { Orientation = Orientation.Horizontal };
                }
            }

            while (i < text.Length)
            {
                // Check for line breaks
                if (text[i] == '\n' || text[i] == '\r')
                {
                    FlushText();
                    AddLineBreak();

                    // Skip \r\n or \n\r combinations
                    if (i + 1 < text.Length && (text[i + 1] == '\n' || text[i + 1] == '\r') && text[i] != text[i + 1])
                    {
                        i++;
                    }
                    i++;
                    continue;
                }

                // Bold **text**
                if (i < text.Length - 1 && text[i] == '*' && text[i + 1] == '*')
                {
                    FlushText();
                    i += 2;
                    var boldText = new StringBuilder();
                    while (i < text.Length - 1 && !(text[i] == '*' && text[i + 1] == '*')) { boldText.Append(text[i]); i++; }
                    if (i < text.Length - 1) i += 2;
                    panel.Children.Add(new SelectableTextBlock
                    {
                        Text = boldText.ToString(),
                        FontWeight = FontWeight.Bold,
                        Foreground = new SolidColorBrush(Colors.White),
                        FontSize = 14,
                        VerticalAlignment = VerticalAlignment.Center,
                        TextWrapping = TextWrapping.Wrap
                    });
                    continue;
                }

                // Inline code `text`
                if (text[i] == '`')
                {
                    FlushText();
                    i++;
                    var codeText = new StringBuilder();
                    while (i < text.Length && text[i] != '`') { codeText.Append(text[i]); i++; }
                    if (i < text.Length) i++;
                    panel.Children.Add(new SelectableTextBlock
                    {
                        Text = codeText.ToString(),
                        FontFamily = new FontFamily("Consolas,Courier New,monospace"),
                        Foreground = new SolidColorBrush(Color.Parse("#d4d4d4")),
                        FontSize = 14,
                        VerticalAlignment = VerticalAlignment.Center,
                        TextWrapping = TextWrapping.Wrap
                    });
                    continue;
                }

                // Links [text](url)
                if (text[i] == '[')
                {
                    var linkMatch = System.Text.RegularExpressions.Regex.Match(text.Substring(i), @"^\[([^\]]+)\]\(([^\)]+)\)");
                    if (linkMatch.Success)
                    {
                        FlushText();
                        var linkText = linkMatch.Groups[1].Value;
                        var linkUrl = linkMatch.Groups[2].Value;

                        var linkButton = new Button
                        {
                            Content = linkText,
                            Foreground = new SolidColorBrush(Color.Parse("#0969da")),
                            Background = Brushes.Transparent,
                            BorderThickness = new Thickness(0),
                            Padding = new Thickness(0),
                            Cursor = new Cursor(StandardCursorType.Hand),
                            FontSize = 14,
                            VerticalAlignment = VerticalAlignment.Center,
                            Tag = linkUrl
                        };

                        linkButton.Click += (s, e) =>
                        {
                            if (linkButton.Tag is string url)
                            {
                                try { OpenUrl(url); } catch { }
                            }
                        };

                        panel.Children.Add(linkButton);
                        i += linkMatch.Length;
                        continue;
                    }
                }

                currentText.Append(text[i]);
                i++;
            }

            FlushText();

            if (panel.Children.Count > 0)
            {
                blocks.Add(panel);
            }

            return blocks;
        }

        private void FlushListItems(List<Control> controls, List<string> listItems)
        {
            if (listItems.Count > 0)
            {
                var listPanel = new StackPanel { Margin = new Thickness(0, 4, 0, 8) };
                foreach (var item in listItems)
                {
                    var blocks = ParseInlineMarkdown(item);
                    foreach (var block in blocks)
                    {
                        block.Margin = new Thickness(0, 2, 0, 2);
                        listPanel.Children.Add(block);
                    }
                }
                controls.Add(listPanel);
                listItems.Clear();
            }
        }

        private async Task<string> FetchChangelogAsync(string repository)
        {
            try
            {
                using var client = new HttpClient();
                client.DefaultRequestHeaders.Add("User-Agent", "GithubLauncher");

                if (!string.IsNullOrEmpty(_settings?.GitHubApiToken))
                {
                    client.DefaultRequestHeaders.Add("Authorization", $"token {_settings.GitHubApiToken}");
                }

                var url = $"https://api.github.com/repos/{repository}/releases/latest";
                var response = await client.GetAsync(url);

                if (!response.IsSuccessStatusCode)
                {
                    return "Failed to fetch changelog from GitHub.";
                }

                var json = await response.Content.ReadAsStringAsync();
                using var document = JsonDocument.Parse(json);
                var root = document.RootElement;

                if (root.TryGetProperty("body", out var bodyElement))
                {
                    var body = bodyElement.GetString();
                    if (!string.IsNullOrEmpty(body))
                    {
                        return body;
                    }
                }

                return "No changelog available for this release.";
            }
            catch (Exception ex)
            {
                return $"Error fetching changelog: {ex.Message}";
            }
        }

        private void CloseChangelog()
        {
            _isChangelogOpen = false;
            _currentChangelogGame = null;

            // Hide changelog panel
            var changelogPanel = this.FindControl<Border>("ChangelogPanel");
            if (changelogPanel != null)
            {
                changelogPanel.IsVisible = false;
            }

            // Restore sidebar content
            var sidebarContent = this.FindControl<StackPanel>("SidebarContent");
            if (sidebarContent != null)
            {
                sidebarContent.Width = double.NaN;
            }

            // Restore header title
            var headerTitle = this.FindControl<TextBlock>("HeaderTitleText");
            if (headerTitle != null)
            {
                headerTitle.Text = "Library";
            }
        }

        private async void CreateShortcut_Click(object sender, RoutedEventArgs e)
        {
            var menuItem = sender as MenuItem;
            var game = menuItem?.CommandParameter as GameInfo;

            if (game == null)
            {
                await ShowMessageBoxAsync("Unable to identify the selected app.", "Error");
                return;
            }

            try
            {
                string launcherPath = Process.GetCurrentProcess().MainModule?.FileName ?? "";
                if (string.IsNullOrEmpty(launcherPath))
                {
                    await ShowMessageBoxAsync("Could not determine launcher location.", "Error");
                    return;
                }

                GithubLauncher.Services.ShortcutHelper.CreateGameShortcut(
                    game,
                    launcherPath,
                    _gameManager.CacheFolder);

            }
            catch (Exception ex)
            {
                await ShowMessageBoxAsync($"Failed to create shortcut: {ex.Message}", "Error");
            }
        }

        private async void AddToSteam_Click(object sender, RoutedEventArgs e)
        {
            var menuItem = sender as MenuItem;
            var game = menuItem?.CommandParameter as GameInfo;

            if (game == null)
            {
                await ShowMessageBoxAsync("Unable to identify the selected app.", "Error");
                return;
            }

            if (OperatingSystem.IsMacOS())
            {
                await ConfigureMacLaunchAsync(game);
                return;
            }

            try
            {
                string launcherPath = Process.GetCurrentProcess().MainModule?.FileName ?? "";
                if (string.IsNullOrEmpty(launcherPath))
                {
                    await ShowMessageBoxAsync("Could not determine launcher location.", "Error");
                    return;
                }

                string resultMessage = GithubLauncher.Services.ShortcutHelper.IsSteamRunning()
                    ? GithubLauncher.Services.ShortcutHelper.QueueGameAddToSteam(game, launcherPath)
                    : GithubLauncher.Services.ShortcutHelper.AddGameToSteam(
                        game,
                        launcherPath,
                        _gameManager.CacheFolder);

                await ShowMessageBoxAsync(resultMessage, "Steam Shortcut");
            }
            catch (Exception ex)
            {
                await ShowMessageBoxAsync($"Failed to add the game to Steam: {ex.Message}", "Error");
            }
        }

        public new event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string propertyName = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private void ApplyRoundedCorners()
        {
            if (!OperatingSystem.IsWindows())
                return;

            try
            {
                var hwnd = this.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
                if (hwnd != IntPtr.Zero)
                {
                    const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
                    int DWMWCP_ROUND = 2;

                    DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref DWMWCP_ROUND, sizeof(int));
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to apply rounded corners: {ex.Message}");
            }
        }

        [DllImport("dwmapi.dll", SetLastError = true)]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);
    }

    public class MarkdownBlock
    {
        public string Type { get; set; } = "paragraph";
        public string Content { get; set; } = "";
        public int Level { get; set; } = 0;
        public List<string> Items { get; set; } = new List<string>();
    }

}

