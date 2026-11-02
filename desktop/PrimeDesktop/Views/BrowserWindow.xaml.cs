using System.Windows.Media.Animation;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Microsoft.Win32;
using PrimeDesktop.Models;
using PrimeDesktop.Services;
using PrimeDesktop.ViewModels;

namespace PrimeDesktop.Views;

public class HiddenToolbarItemViewModel
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public BrowserToolbarItemConfig Config { get; set; } = null!;
}

public partial class BrowserWindow : Window
{
    private const string MobileUserAgent = "Mozilla/5.0 (Linux; Android 14; Pixel 8) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/128.0.0.0 Mobile Safari/537.36";
    private const string DesktopUserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/128.0.0.0 Safari/537.36";

    private static CoreWebView2Environment? _sharedEnvironment;

    private readonly MainViewModel? _vm;
    private bool _isDesktopMode;
    private bool _isFullScreen;
    private bool _isToolbarEditMode;
    private bool _isAddressBarInTabRow;
    private WindowState _previousWindowState = WindowState.Normal;

    public ObservableCollection<BrowserTabItem> Tabs { get; } = new();
    public BrowserTabItem? ActiveTab { get; private set; }

    public ObservableCollection<BrowserToolbarItemConfig> ToolbarItems { get; } = new();
    public ObservableCollection<HiddenToolbarItemViewModel> HiddenToolbarButtons { get; } = new();
    public ObservableCollection<BrowserExtensionItem> InstalledExtensions { get; } = new();

    private readonly Stack<string> _recentlyClosedTabs = new();

    // Reusable UI Action Buttons for dynamic toolbar placement
    private Button _btnBack = null!;
    private Button _btnForward = null!;
    private Button _btnRefresh = null!;
    private Button _btnHome = null!;
    private Button _btnToggleDesktopMode = null!;
    private Button _btnExtensions = null!;
    private Button _btnOpenExternal = null!;
    private Button _btnCustomizeToolbar = null!;
    private Button _btnBrowserSettings = null!;
    private TextBlock _txtDesktopModeIcon = null!;

    // Tab hover preview (Edge/Chrome style: title, thumbnail, RAM usage after 2s hover)
    private readonly DispatcherTimer _tabHoverTimer = new()
    {
        Interval = TimeSpan.FromSeconds(2.0)
    };
    private FrameworkElement? _hoveredTabElement;
    private BrowserTabItem? _hoveredTabItem;

    // Drag and drop state
    private Point _dragStartPoint;
    private BrowserToolbarItemConfig? _draggedItem;
    private readonly Border _dropIndicator = new Border
    {
        BorderBrush = (Brush)Application.Current.FindResource("AccentBrush"),
        BorderThickness = new Thickness(2),
        CornerRadius = new CornerRadius(8),
        Background = new SolidColorBrush(Color.FromArgb(65, 0, 188, 212)),
        Height = 32,
        Width = 34,
        Margin = new Thickness(3, 0, 3, 0),
        IsHitTestVisible = false
    };

    public static void PrewarmEnvironment()
    {
    }

    public BrowserWindow(string? initialUrl = null, MainViewModel? vm = null)
    {
        InitializeComponent();
        _vm = vm ?? Application.Current?.MainWindow?.DataContext as MainViewModel;

        TabsItemsControl.ItemsSource = Tabs;
        MiniVerticalTabsItemsControl.ItemsSource = Tabs;
        VerticalTabsItemsControl.ItemsSource = Tabs;
        ExtensionsItemsControl.ItemsSource = InstalledExtensions;

        // Populate search engine combo
        CmbSearchEngine.ItemsSource = new[] { "Google", "Яндекс", "DuckDuckGo", "Bing" };
        if (_vm?.Settings != null)
        {
            CmbSearchEngine.SelectedItem = _vm.Settings.SearchEngine;
            ChkEnableJs.IsChecked = _vm.Settings.BrowserEnableJs;
            ChkEnableCookies.IsChecked = _vm.Settings.BrowserEnableCookies;
            ChkDesktopMode.IsChecked = _vm.Settings.BrowserDesktopMode;
            _isDesktopMode = _vm.Settings.BrowserDesktopMode;
        }
        else
        {
            CmbSearchEngine.SelectedItem = "Google";
            ChkEnableJs.IsChecked = true;
            ChkEnableCookies.IsChecked = true;
            _isDesktopMode = false;
        }

        if (_vm?.Settings != null)
        {
            _isAddressBarInTabRow = _vm.Settings.BrowserAddressBarInTabRow;
        }

        // Initialize Toolbar buttons
        InitializeActionButtons();
        LoadToolbarConfig();
        RebuildToolbar();

        UpdateDesktopModeUi();

        // Apply native Windows Fluent transitions and DWM frame
        PrimeAnimationService.HookBrandTransitions(this);

        HiddenButtonsItemsControl.ItemsSource = HiddenToolbarButtons;

        // Apply tab layout orientation (horizontal vs vertical)
        ApplyTabsOrientation(_vm?.Settings?.BrowserVerticalTabs == true);
        if (_vm?.Settings != null)
        {
            _vm.Settings.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(AppSettings.BrowserVerticalTabs))
                {
                    Dispatcher.Invoke(() => ApplyTabsOrientation(_vm.Settings.BrowserVerticalTabs));
                }
            };
        }

        // Open initial tab if URL was given, otherwise open Start Search Screen tab
        if (!string.IsNullOrWhiteSpace(initialUrl))
        {
            AddNewTab(initialUrl);
        }
        else
        {
            AddNewTab("about:home");
        }

        KeyDown += BrowserWindow_KeyDown;
        BrowserHostGrid.PreviewMouseWheel += BrowserHostGrid_PreviewMouseWheel;
        _tabHoverTimer.Tick += TabHoverTimer_Tick;
    }

    private static async Task<CoreWebView2Environment?> GetOrInitEnvironmentAsync()
    {
        if (_sharedEnvironment != null) return _sharedEnvironment;

        try
        {
            string userDataDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PrimeDesktop", "WebView2Data");
            Directory.CreateDirectory(userDataDir);

            var options = new CoreWebView2EnvironmentOptions
            {
                AreBrowserExtensionsEnabled = true
            };

            _sharedEnvironment = await CoreWebView2Environment.CreateAsync(null, userDataDir, options);
            return _sharedEnvironment;
        }
        catch (Exception ex)
        {
            App.Log($"[Browser] Initialize environment error: {ex.Message}");
            return null;
        }
    }

    #region Action Buttons Initialization & Toolbar Builder

    private void InitializeActionButtons()
    {
        var navBtnStyle = (Style)FindResource("AddressNavButton");

        // Back
        _btnBack = new Button
        {
            Style = navBtnStyle,
            ToolTip = "Назад (Alt+Left)",
            Margin = new Thickness(0),
            Content = new System.Windows.Shapes.Path
            {
                Data = (Geometry)FindResource("IconArrowLeft"),
                Width = 14,
                Height = 14,
                Fill = (Brush)FindResource("TextPrimaryBrush"),
                Stretch = Stretch.Uniform
            }
        };
        _btnBack.Click += BtnBack_Click;

        // Forward
        _btnForward = new Button
        {
            Style = navBtnStyle,
            ToolTip = "Вперед (Alt+Right)",
            Margin = new Thickness(0),
            Content = new System.Windows.Shapes.Path
            {
                Data = (Geometry)FindResource("IconArrowRight"),
                Width = 14,
                Height = 14,
                Fill = (Brush)FindResource("TextPrimaryBrush"),
                Stretch = Stretch.Uniform
            }
        };
        _btnForward.Click += BtnForward_Click;

        // Refresh
        _btnRefresh = new Button
        {
            Style = navBtnStyle,
            ToolTip = "Обновить страницу (F5)",
            Margin = new Thickness(0),
            Content = new System.Windows.Shapes.Path
            {
                Data = (Geometry)FindResource("IconRefresh"),
                Width = 13,
                Height = 13,
                Fill = (Brush)FindResource("TextPrimaryBrush"),
                Stretch = Stretch.Uniform
            }
        };
        _btnRefresh.Click += BtnRefresh_Click;

        // Home Page (Start Screen)
        _btnHome = new Button
        {
            Style = navBtnStyle,
            ToolTip = "Главная страница поиска (Alt+Home)",
            Margin = new Thickness(0),
            Content = new System.Windows.Shapes.Path
            {
                Data = (Geometry)FindResource("IconHome"),
                Width = 14,
                Height = 14,
                Fill = (Brush)FindResource("TextPrimaryBrush"),
                Stretch = Stretch.Uniform
            }
        };
        _btnHome.Click += (s, e) =>
        {
            NavigateCurrentTab("about:home");
        };

        // Desktop / Mobile Mode Toggle
        _txtDesktopModeIcon = new TextBlock
        {
            Text = "💻",
            FontSize = 14,
            VerticalAlignment = VerticalAlignment.Center
        };
        _btnToggleDesktopMode = new Button
        {
            Style = navBtnStyle,
            ToolTip = "Переключить Режим ПК / Мобильный вид",
            Margin = new Thickness(0),
            Content = _txtDesktopModeIcon
        };
        _btnToggleDesktopMode.Click += BtnToggleDesktopMode_Click;

        // Extensions (Chrome)
        _btnExtensions = new Button
        {
            Style = navBtnStyle,
            ToolTip = "Расширения Chrome (Ctrl+Shift+E)",
            Margin = new Thickness(0),
            Content = new System.Windows.Shapes.Path
            {
                Data = (Geometry)FindResource("IconExtension"),
                Width = 14,
                Height = 14,
                Fill = (Brush)FindResource("TextPrimaryBrush"),
                Stretch = Stretch.Uniform
            }
        };
        _btnExtensions.Click += BtnExtensions_Click;

        // Open in External Browser
        _btnOpenExternal = new Button
        {
            Style = navBtnStyle,
            ToolTip = "Открыть во внешнем системном браузере",
            Margin = new Thickness(0),
            Content = new System.Windows.Shapes.Path
            {
                Data = (Geometry)FindResource("IconOpenInNew"),
                Width = 13,
                Height = 13,
                Fill = (Brush)FindResource("TextPrimaryBrush"),
                Stretch = Stretch.Uniform
            }
        };
        _btnOpenExternal.Click += BtnOpenExternal_Click;

        // Toolbar Pencil Customizer
        _btnCustomizeToolbar = new Button
        {
            Style = navBtnStyle,
            ToolTip = "Настройка кнопок браузера (Карандаш)",
            Margin = new Thickness(0),
            Content = new System.Windows.Shapes.Path
            {
                Data = (Geometry)FindResource("IconEdit"),
                Width = 13,
                Height = 13,
                Fill = (Brush)FindResource("TextPrimaryBrush"),
                Stretch = Stretch.Uniform
            }
        };
        _btnCustomizeToolbar.Click += BtnCustomizeToolbar_Click;

        // Browser Settings
        _btnBrowserSettings = new Button
        {
            Style = navBtnStyle,
            ToolTip = "Настройки встроенного браузера",
            Margin = new Thickness(0),
            Content = new System.Windows.Shapes.Path
            {
                Data = (Geometry)FindResource("IconSettings"),
                Width = 14,
                Height = 14,
                Fill = (Brush)FindResource("TextPrimaryBrush"),
                Stretch = Stretch.Uniform
            }
        };
        _btnBrowserSettings.Click += BtnBrowserSettings_Click;
    }

    private static void DisconnectFromParent(FrameworkElement? element)
    {
        if (element == null) return;
        if (element.Parent is Panel panel)
        {
            panel.Children.Remove(element);
        }
        else if (element.Parent is Decorator decorator)
        {
            decorator.Child = null;
        }
        else if (element.Parent is ContentControl contentControl)
        {
            contentControl.Content = null;
        }
    }

    private FrameworkElement CreateSpacerElement(string id)
    {
        var spacerBorder = new Border
        {
            Height = 32,
            Tag = id
        };

        if (_isToolbarEditMode)
        {
            spacerBorder.Width = 42;
            spacerBorder.Background = (Brush)FindResource("HeaderBrush");
            spacerBorder.BorderBrush = (Brush)FindResource("BorderBrush");
            spacerBorder.BorderThickness = new Thickness(1);
            spacerBorder.CornerRadius = new CornerRadius(6);
            spacerBorder.ToolTip = "Отступ (перетащите для перемещения, ✕ для удаления)";
            spacerBorder.Margin = new Thickness(3, 0, 3, 0);

            var spContent = new Grid();
            var icon = new TextBlock
            {
                Text = "↔",
                FontSize = 13,
                FontWeight = FontWeights.Bold,
                Foreground = (Brush)FindResource("TextSecondaryBrush"),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                IsHitTestVisible = false
            };
            spContent.Children.Add(icon);

            var delBtn = new Button
            {
                Width = 16,
                Height = 16,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 1, 1, 0),
                Padding = new Thickness(0),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                ToolTip = "Удалить отступ",
                Content = new Border
                {
                    CornerRadius = new CornerRadius(8),
                    Background = new SolidColorBrush(Color.FromArgb(220, 244, 67, 54)),
                    Child = new System.Windows.Shapes.Path
                    {
                        Data = (Geometry)FindResource("IconClose"),
                        Width = 6,
                        Height = 6,
                        Fill = Brushes.White,
                        Stretch = Stretch.Uniform,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    }
                }
            };
            delBtn.Click += (s, e) =>
            {
                var itemToRemove = ToolbarItems.FirstOrDefault(x => x.Id == id);
                if (itemToRemove != null)
                {
                    ToolbarItems.Remove(itemToRemove);
                    RebuildToolbar();
                    SaveToolbarConfig();
                }
                e.Handled = true;
            };
            spContent.Children.Add(delBtn);

            var cm = new ContextMenu();
            var deleteItem = new MenuItem { Header = "✕ Удалить отступ" };
            deleteItem.Click += (s, e) =>
            {
                var itemToRemove = ToolbarItems.FirstOrDefault(x => x.Id == id);
                if (itemToRemove != null)
                {
                    ToolbarItems.Remove(itemToRemove);
                    RebuildToolbar();
                    SaveToolbarConfig();
                }
            };
            cm.Items.Add(deleteItem);
            spacerBorder.ContextMenu = cm;
            spacerBorder.Child = spContent;
        }
        else
        {
            spacerBorder.Width = 24;
            spacerBorder.Background = Brushes.Transparent;
            spacerBorder.BorderThickness = new Thickness(0);
        }

        return spacerBorder;
    }

    private FrameworkElement? GetElementForId(string id)
    {
        if (id.StartsWith("spacer"))
        {
            return CreateSpacerElement(id);
        }

        return id switch
        {
            "home" => _btnHome,
            "back" => _btnBack,
            "forward" => _btnForward,
            "refresh" => _btnRefresh,
            "address_bar" => CardAddressBar,
            "desktop_mode" => _btnToggleDesktopMode,
            "extensions" => _btnExtensions,
            "external" => _btnOpenExternal,
            "customize" => _btnCustomizeToolbar,
            "settings" => _btnBrowserSettings,
            _ => null
        };
    }

    private void LoadToolbarConfig()
    {
        ToolbarItems.Clear();
        List<BrowserToolbarItemConfig>? loaded = null;

        if (!string.IsNullOrWhiteSpace(_vm?.Settings?.BrowserToolbarLayout))
        {
            try
            {
                loaded = JsonSerializer.Deserialize<List<BrowserToolbarItemConfig>>(_vm.Settings.BrowserToolbarLayout);
            }
            catch { }
        }

        if (loaded == null || loaded.Count == 0)
        {
            loaded = BrowserToolbarItemConfig.GetDefaultToolbar();
        }

        // Ensure home button exists
        if (!loaded.Any(x => x.Id == "home"))
        {
            int refIdx = loaded.FindIndex(x => x.Id == "refresh");
            int insertIdx = (refIdx >= 0) ? refIdx + 1 : 0;
            loaded.Insert(insertIdx, new BrowserToolbarItemConfig
            {
                Id = "home",
                Name = "Главная страница поиска",
                Icon = "IconHome",
                Placement = "left",
                Order = insertIdx,
                IsVisible = true
            });
        }

        // Ensure address_bar defaults to center placement if not in tabs row
        var addrItem = loaded.FirstOrDefault(x => x.Id == "address_bar");
        if (addrItem != null && !addrItem.Placement.StartsWith("tabs_"))
        {
            addrItem.Placement = "center";
        }

        for (int i = 0; i < loaded.Count; i++)
        {
            loaded[i].Order = i;
        }

        foreach (var item in loaded.OrderBy(x => x.Order))
        {
            ToolbarItems.Add(item);
        }
    }

    private void SaveToolbarConfig()
    {
        if (_vm?.Settings == null) return;
        try
        {
            _vm.Settings.BrowserToolbarLayout = JsonSerializer.Serialize(ToolbarItems.ToList());
        }
        catch { }
    }

    private void RebuildToolbar()
    {
        TabsToolbarLeftPanel.Children.Clear();
        TabsToolbarRightPanel.Children.Clear();
        ToolbarLeftPanel.Children.Clear();
        ToolbarRightPanel.Children.Clear();
        ToolbarAddressBarHost.Child = null;

        // In edit mode: make address bar non-interactive for typing so clicks can drag it
        TxtAddress.IsReadOnly = _isToolbarEditMode;
        TxtAddress.IsHitTestVisible = !_isToolbarEditMode;
        CardAddressBar.Cursor = _isToolbarEditMode ? Cursors.Hand : Cursors.Arrow;
        BtnGo.IsEnabled = !_isToolbarEditMode;
        BtnClearAddress.IsEnabled = !_isToolbarEditMode;

        // Ensure panels allow drop only during edit mode
        TabsToolbarLeftPanel.AllowDrop = _isToolbarEditMode;
        TabsToolbarRightPanel.AllowDrop = _isToolbarEditMode;
        ToolbarLeftPanel.AllowDrop = _isToolbarEditMode;
        ToolbarRightPanel.AllowDrop = _isToolbarEditMode;
        ToolbarAddressBarHost.AllowDrop = _isToolbarEditMode;

        // Explicitly enable all toolbar action buttons when in normal mode
        if (!_isToolbarEditMode)
        {
            if (_btnHome != null) _btnHome.IsEnabled = true;
            if (_btnRefresh != null) _btnRefresh.IsEnabled = true;
            if (_btnToggleDesktopMode != null) _btnToggleDesktopMode.IsEnabled = true;
            if (_btnExtensions != null) _btnExtensions.IsEnabled = true;
            if (_btnOpenExternal != null) _btnOpenExternal.IsEnabled = true;
            if (_btnCustomizeToolbar != null) _btnCustomizeToolbar.IsEnabled = true;
            if (_btnBrowserSettings != null) _btnBrowserSettings.IsEnabled = true;
            UpdateNavigationButtonsState();
        }

        foreach (var item in ToolbarItems.OrderBy(x => x.Order))
        {
            if (item.Id == "settings" || item.Id == "address_bar")
            {
                item.IsVisible = true;
            }

            // In normal mode: skip hidden buttons. In edit mode: show all buttons with eye indicator!
            if (!item.IsVisible && !_isToolbarEditMode) continue;

            var element = GetElementForId(item.Id);
            if (element == null) continue;

            DisconnectFromParent(element);

            if (item.Id == "address_bar")
            {
                if (item.Placement.StartsWith("tabs_"))
                {
                    CardAddressBar.Width = 300;
                    CardAddressBar.HorizontalAlignment = HorizontalAlignment.Left;
                }
                else if (item.Placement == "left" || item.Placement == "right")
                {
                    CardAddressBar.Width = 460;
                    CardAddressBar.HorizontalAlignment = (item.Placement == "left") ? HorizontalAlignment.Left : HorizontalAlignment.Right;
                }
                else
                {
                    CardAddressBar.Width = double.NaN;
                    CardAddressBar.HorizontalAlignment = HorizontalAlignment.Stretch;
                }
            }

            // Wrapper container for drag-and-drop & eye badge
            var container = new Grid
            {
                Margin = new Thickness(1, 0, 1, 0),
                Tag = item
            };

            // In edit mode: allow dragging
            if (_isToolbarEditMode)
            {
                container.AllowDrop = true;
                container.PreviewMouseLeftButtonDown += ToolbarItem_PreviewMouseLeftButtonDown;
                container.PreviewMouseMove += ToolbarItem_PreviewMouseMove;
                container.DragOver += ToolbarItem_DragOver;
                container.Drop += ToolbarItem_Drop;
                container.Cursor = Cursors.Hand;
            }

            // Button opacity: semi-transparent if hidden during edit mode
            element.Opacity = (!item.IsVisible && _isToolbarEditMode) ? 0.40 : 1.0;
            // In edit mode, disable hit test on button so drag-and-drop works cleanly and mouse clicks are not swallowed
            element.IsHitTestVisible = !_isToolbarEditMode;
            container.Children.Add(element);

            // In edit mode: add the eye button in the bottom-right corner (except Settings, Address Bar, and Spacers)!
            if (_isToolbarEditMode && item.Id != "settings" && item.Id != "address_bar" && !item.Id.StartsWith("spacer"))
            {
                var eyeBorder = new Border
                {
                    CornerRadius = new CornerRadius(7),
                    Background = item.IsVisible
                        ? (Brush)FindResource("CardBrush")
                        : new SolidColorBrush(Color.FromArgb(200, 244, 67, 54)),
                    BorderBrush = (Brush)FindResource("BorderBrush"),
                    BorderThickness = new Thickness(1)
                };

                var eyeIcon = new System.Windows.Shapes.Path
                {
                    Data = (Geometry)FindResource(item.IsVisible ? "IconEye" : "IconEyeOff"),
                    Width = 8,
                    Height = 8,
                    Fill = item.IsVisible ? (Brush)FindResource("AccentBrush") : Brushes.White,
                    Stretch = Stretch.Uniform,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                eyeBorder.Child = eyeIcon;

                var eyeBtn = new Button
                {
                    Width = 14,
                    Height = 14,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Bottom,
                    Margin = new Thickness(0, 0, -1, -1),
                    Padding = new Thickness(0),
                    Background = Brushes.Transparent,
                    BorderThickness = new Thickness(0),
                    Cursor = Cursors.Hand,
                    ToolTip = item.IsVisible ? "Нажмите, чтобы скрыть кнопку" : "Нажмите, чтобы отобразить кнопку",
                    Content = eyeBorder,
                    Tag = item
                };

                eyeBtn.Click += (s, e) =>
                {
                    item.IsVisible = !item.IsVisible;
                    RebuildToolbar();
                    SaveToolbarConfig();
                    e.Handled = true;
                };

                container.Children.Add(eyeBtn);
            }

            if (item.Placement == "tabs_left")
            {
                TabsToolbarLeftPanel.Children.Add(container);
            }
            else if (item.Placement == "tabs_right")
            {
                TabsToolbarRightPanel.Children.Add(container);
            }
            else if (item.Placement == "left")
            {
                ToolbarLeftPanel.Children.Add(container);
            }
            else if (item.Placement == "right")
            {
                ToolbarRightPanel.Children.Add(container);
            }
            else // "center"
            {
                ToolbarAddressBarHost.Child = container;
            }
        }

        bool hasRow1 = ToolbarLeftPanel.Children.Count > 0 || ToolbarRightPanel.Children.Count > 0 || ToolbarAddressBarHost.Child != null || _isToolbarEditMode;
        RowAddressBar.Height = hasRow1 ? GridLength.Auto : new GridLength(0);
        ToolbarRow1.Visibility = hasRow1 ? Visibility.Visible : Visibility.Collapsed;

        UpdateHiddenButtonsInSettingsPopup();
    }

    #endregion

    #region Drag & Drop Toolbar Items Handlers with Contour Preview

    private void ToolbarItem_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!_isToolbarEditMode) return;
        _dragStartPoint = e.GetPosition(this);
        if (sender is FrameworkElement fe && fe.Tag is BrowserToolbarItemConfig item)
        {
            _draggedItem = item;
        }
    }

    private void ToolbarItem_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isToolbarEditMode || _draggedItem == null || e.LeftButton != MouseButtonState.Pressed) return;

        Point curPos = e.GetPosition(this);
        Vector diff = _dragStartPoint - curPos;
        if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
            Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
        {
            _dropIndicator.Width = (_draggedItem.Id == "address_bar") ? 160 : 34;
            var data = new DataObject("PrimeToolbarItem", _draggedItem);
            try
            {
                DragDrop.DoDragDrop((DependencyObject)sender, data, DragDropEffects.Move);
            }
            finally
            {
                _draggedItem = null;
                RemoveDropIndicator();
                Mouse.Capture(null);
            }
        }
    }

    private void ToolbarItem_DragOver(object sender, DragEventArgs e)
    {
        if (!_isToolbarEditMode || !e.Data.GetDataPresent("PrimeToolbarItem")) return;
        e.Effects = DragDropEffects.Move;
        e.Handled = true;

        if (sender is FrameworkElement fe && fe.Parent is Panel parentPanel)
        {
            UpdateDropIndicator(parentPanel, e.GetPosition(parentPanel));
        }
    }

    private void ToolbarItem_Drop(object sender, DragEventArgs e)
    {
        ToolbarPanel_Drop(sender, e);
    }

    private void ToolbarPanel_DragOver(object sender, DragEventArgs e)
    {
        if (!_isToolbarEditMode || !e.Data.GetDataPresent("PrimeToolbarItem")) return;
        e.Effects = DragDropEffects.Move;
        e.Handled = true;

        if (sender is Panel panel)
        {
            UpdateDropIndicator(panel, e.GetPosition(panel));
        }
    }

    private void ToolbarPanel_DragLeave(object sender, DragEventArgs e)
    {
        if (sender is FrameworkElement fe)
        {
            Point p = e.GetPosition(fe);
            if (p.X < 0 || p.Y < 0 || p.X > fe.ActualWidth || p.Y > fe.ActualHeight)
            {
                RemoveDropIndicator();
            }
        }
    }

    private void ToolbarPanel_Drop(object sender, DragEventArgs e)
    {
        RemoveDropIndicator();
        if (!_isToolbarEditMode || !e.Data.GetDataPresent("PrimeToolbarItem")) return;
        var dropped = e.Data.GetData("PrimeToolbarItem") as BrowserToolbarItemConfig;
        if (dropped == null) return;

        if (sender == ToolbarAddressBarHost || sender == CardAddressBar || (sender is FrameworkElement feCenter && feCenter.Parent == ToolbarAddressBarHost))
        {
            dropped.Placement = "center";
            ToolbarItems.Remove(dropped);
            ToolbarItems.Add(dropped);
            for (int i = 0; i < ToolbarItems.Count; i++) ToolbarItems[i].Order = i;
            RebuildToolbar();
            SaveToolbarConfig();
            e.Handled = true;
            return;
        }

        Panel? targetPanel = null;
        if (sender is Panel p)
        {
            targetPanel = p;
        }
        else if (sender is FrameworkElement fe && fe.Parent is Panel parentP)
        {
            targetPanel = parentP;
        }

        if (targetPanel == null) return;

        string targetPlacement = "left";
        if (targetPanel == TabsToolbarLeftPanel) targetPlacement = "tabs_left";
        else if (targetPanel == TabsToolbarRightPanel) targetPlacement = "tabs_right";
        else if (targetPanel == ToolbarRightPanel) targetPlacement = "right";
        else targetPlacement = "left";

        dropped.Placement = targetPlacement;

        Point mousePos = e.GetPosition(targetPanel);
        int dropIdx = GetDropIndex(targetPanel, mousePos);

        ToolbarItems.Remove(dropped);
        var panelItems = ToolbarItems.Where(i => i.Placement == targetPlacement).ToList();
        dropIdx = Math.Clamp(dropIdx, 0, panelItems.Count);

        int insertIndex;
        if (dropIdx < panelItems.Count)
        {
            insertIndex = ToolbarItems.IndexOf(panelItems[dropIdx]);
        }
        else if (panelItems.Count > 0)
        {
            insertIndex = ToolbarItems.IndexOf(panelItems.Last()) + 1;
        }
        else
        {
            insertIndex = ToolbarItems.Count;
        }

        ToolbarItems.Insert(Math.Clamp(insertIndex, 0, ToolbarItems.Count), dropped);

        for (int i = 0; i < ToolbarItems.Count; i++)
        {
            ToolbarItems[i].Order = i;
        }

        RebuildToolbar();
        SaveToolbarConfig();
        e.Handled = true;
    }

    private void UpdateDropIndicator(Panel panel, Point mousePos)
    {
        if (_dropIndicator == null) return;
        _dropIndicator.IsHitTestVisible = false;
        _dropIndicator.Width = (_draggedItem?.Id == "address_bar") ? 160 : 34;

        int targetIdx = GetDropIndex(panel, mousePos);

        if (_dropIndicator.Parent is Panel oldP && oldP != panel)
        {
            oldP.Children.Remove(_dropIndicator);
        }

        if (!panel.Children.Contains(_dropIndicator))
        {
            targetIdx = Math.Clamp(targetIdx, 0, panel.Children.Count);
            panel.Children.Insert(targetIdx, _dropIndicator);
        }
        else
        {
            int curIdx = panel.Children.IndexOf(_dropIndicator);
            if (curIdx != targetIdx)
            {
                panel.Children.Remove(_dropIndicator);
                targetIdx = Math.Clamp(targetIdx, 0, panel.Children.Count);
                panel.Children.Insert(targetIdx, _dropIndicator);
            }
        }
    }

    private int GetDropIndex(Panel panel, Point mousePos)
    {
        int index = 0;
        foreach (UIElement child in panel.Children)
        {
            if (child == _dropIndicator) continue;
            if (child is FrameworkElement fe)
            {
                Point pos = fe.TranslatePoint(new Point(0, 0), panel);
                double midX = pos.X + fe.ActualWidth / 2.0;
                if (mousePos.X > midX)
                {
                    index++;
                }
                else
                {
                    break;
                }
            }
        }
        return index;
    }

    private void RemoveDropIndicator()
    {
        if (_dropIndicator.Parent is Panel p)
        {
            p.Children.Remove(_dropIndicator);
        }
    }

    #endregion

    #region Tab Management & WebView2 Initialization

    public void AddNewTab(string? initialUrl = null)
    {
        bool isStart = string.IsNullOrWhiteSpace(initialUrl) ||
                       initialUrl.Equals("about:home", StringComparison.OrdinalIgnoreCase) ||
                       initialUrl.Equals("about:blank", StringComparison.OrdinalIgnoreCase);

        var tab = new BrowserTabItem
        {
            Title = isStart ? "Новая вкладка" : "Загрузка...",
            Url = isStart ? "about:home" : BuildUrlOrSearch(initialUrl!),
            IsLoading = !isStart
        };

        Tabs.Add(tab);
        UpdateTabsCounter();
        AnimateTabsFlyUp();

        if (!isStart)
        {
            var webView = new WebView2
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                DefaultBackgroundColor = System.Drawing.Color.FromArgb(255, 12, 16, 23)
            };

            tab.WebView = webView;
            BrowserHostGrid.Children.Add(webView);
            _ = InitializeWebViewForTabAsync(tab, webView, tab.Url);
        }

        ActivateTab(tab);
    }

    private async Task InitializeWebViewForTabAsync(BrowserTabItem tab, WebView2 webView, string targetUrl)
    {
        try
        {
            var env = await GetOrInitEnvironmentAsync();
            if (env != null)
            {
                await webView.EnsureCoreWebView2Async(env);
            }
            else
            {
                await webView.EnsureCoreWebView2Async();
            }

            ApplyTabSettings(tab);

            // HTML5 Fullscreen video handler (YouTube, Twitch, Netflix, etc.)
            webView.CoreWebView2.ContainsFullScreenElementChanged += (s, e) =>
            {
                Dispatcher.Invoke(() =>
                {
                    if (tab == ActiveTab)
                    {
                        SetFullScreen(webView.CoreWebView2.ContainsFullScreenElement);
                    }
                });
            };

            webView.CoreWebView2.HistoryChanged += (s, e) =>
            {
                Dispatcher.Invoke(() =>
                {
                    if (tab == ActiveTab)
                    {
                        UpdateNavigationButtonsState();
                    }
                });
            };

            webView.NavigationStarting += (s, e) =>
            {
                AnimateTabsFlyUp();
                Dispatcher.Invoke(() =>
                {
                    tab.IsLoading = true;
                    tab.Url = e.Uri;
                    if (tab == ActiveTab)
                    {
                        TxtAddress.Text = e.Uri;
                        BrowserProgressBar.Visibility = Visibility.Visible;
                        BrowserProgressBar.IsIndeterminate = true;
                    }
                });
            };

            webView.NavigationCompleted += (s, e) =>
            {
                Dispatcher.Invoke(() =>
                {
                    tab.IsLoading = false;
                    if (tab == ActiveTab)
                    {
                        BrowserProgressBar.Visibility = Visibility.Collapsed;
                        UpdateNavigationButtonsState();
                    }
                    if (e.IsSuccess && !string.IsNullOrWhiteSpace(tab.Url))
                    {
                        BrowserHistoryService.RecordVisit(tab.Url, tab.Title, webView.CoreWebView2?.FaviconUri);
                        if (_isStartScreenVisible) RefreshRecentSites();
                        _ = UpdateTabThumbnailAsync(tab);
                    }
                });
            };

            webView.SourceChanged += (s, e) =>
            {
                Dispatcher.Invoke(() =>
                {
                    string currentUri = webView.Source?.ToString() ?? "";
                    tab.Url = currentUri;
                    tab.IsSecure = currentUri.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

                    if (tab == ActiveTab)
                    {
                        TxtAddress.Text = currentUri;
                        UpdateSecurityIcon(tab.IsSecure);
                        UpdateNavigationButtonsState();
                    }
                });
            };

            webView.CoreWebView2.DocumentTitleChanged += (s, e) =>
            {
                Dispatcher.Invoke(() =>
                {
                    string title = webView.CoreWebView2.DocumentTitle;
                    if (!string.IsNullOrWhiteSpace(title))
                    {
                        tab.Title = title;
                        BrowserHistoryService.RecordVisit(tab.Url, tab.Title, webView.CoreWebView2.FaviconUri);
                        if (_isStartScreenVisible) RefreshRecentSites();
                    }
                });
            };

            webView.CoreWebView2.FaviconChanged += (s, e) =>
            {
                Dispatcher.Invoke(() =>
                {
                    try
                    {
                        string faviconUri = webView.CoreWebView2.FaviconUri;
                        if (!string.IsNullOrEmpty(faviconUri))
                        {
                            var bmp = new BitmapImage();
                            bmp.BeginInit();
                            bmp.UriSource = new Uri(faviconUri);
                            bmp.CacheOption = BitmapCacheOption.OnLoad;
                            bmp.EndInit();
                            tab.Favicon = bmp;
                        }
                    }
                    catch { }
                });
            };

            webView.CoreWebView2.NewWindowRequested += (s, e) =>
            {
                e.Handled = true;
                Dispatcher.Invoke(() =>
                {
                    AddNewTab(e.Uri);
                });
            };

            webView.Source = new Uri(targetUrl);
        }
        catch (Exception ex)
        {
            App.Log($"[Browser] Error initializing WebView2 for tab: {ex.Message}");
            Dispatcher.Invoke(() =>
            {
                tab.Title = "Ошибка загрузки";
                tab.IsLoading = false;
                if (tab == ActiveTab) BrowserProgressBar.Visibility = Visibility.Collapsed;
            });
        }
    }

    private void ApplyTabSettings(BrowserTabItem tab)
    {
        if (tab.WebView?.CoreWebView2?.Settings == null) return;
        var settings = tab.WebView.CoreWebView2.Settings;

        bool jsEnabled = _vm?.Settings?.BrowserEnableJs ?? true;
        settings.IsScriptEnabled = jsEnabled;
        settings.AreDefaultScriptDialogsEnabled = true;
        settings.IsWebMessageEnabled = true;
        settings.UserAgent = _isDesktopMode ? DesktopUserAgent : MobileUserAgent;
    }

    public void ActivateTab(BrowserTabItem tab)
    {
        if (ActiveTab != null && ActiveTab != tab && ActiveTab.WebView?.CoreWebView2 != null && !ActiveTab.IsLoading)
        {
            _ = UpdateTabThumbnailAsync(ActiveTab);
        }

        if (tab == null)
        {
            ActiveTab = null;
            ShowStartScreen(true);
            return;
        }

        bool isStartPage = string.IsNullOrEmpty(tab.Url) ||
                           tab.Url.Equals("about:home", StringComparison.OrdinalIgnoreCase) ||
                           tab.Url.Equals("about:blank", StringComparison.OrdinalIgnoreCase);

        foreach (var t in Tabs)
        {
            bool isCurrent = (t == tab);
            t.IsActive = isCurrent;
            if (t.WebView != null)
            {
                t.WebView.Visibility = (isCurrent && !isStartPage) ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        ActiveTab = tab;

        if (isStartPage)
        {
            ShowStartScreen(true);
            TxtAddress.Text = "";
        }
        else
        {
            ShowStartScreen(false);
            TxtAddress.Text = tab.Url;
            UpdateSecurityIcon(tab.IsSecure);

            if (tab.WebView != null && !BrowserHostGrid.Children.Contains(tab.WebView))
            {
                BrowserHostGrid.Children.Add(tab.WebView);
            }

            // Fullscreen state check
            if (tab.WebView?.CoreWebView2 != null)
            {
                SetFullScreen(tab.WebView.CoreWebView2.ContainsFullScreenElement);
            }
            else
            {
                SetFullScreen(false);
            }
        }

        UpdateNavigationButtonsState();
    }

    private void CloseTab(BrowserTabItem tab)
    {
        CloseTabPreviewPopup();
        int index = Tabs.IndexOf(tab);
        if (index < 0) return;

        if (!string.IsNullOrWhiteSpace(tab.Url) && !tab.Url.StartsWith("about:"))
        {
            _recentlyClosedTabs.Push(tab.Url);
        }

        if (tab.WebView != null)
        {
            BrowserHostGrid.Children.Remove(tab.WebView);
            try
            {
                tab.WebView.Dispose();
            }
            catch { }
        }

        bool wasActive = (tab == ActiveTab || tab.IsActive);
        Tabs.RemoveAt(index);
        UpdateTabsCounter();

        if (Tabs.Count == 0)
        {
            ActiveTab = null;
            ShowStartScreen(true);
            return;
        }

        if (wasActive || ActiveTab == null || !Tabs.Contains(ActiveTab))
        {
            int newIndex = Math.Clamp(index, 0, Tabs.Count - 1);
            ActivateTab(Tabs[newIndex]);
        }
        else
        {
            ActivateTab(ActiveTab);
        }
    }

    private void ReopenLastClosedTab()
    {
        if (_recentlyClosedTabs.Count > 0)
        {
            string url = _recentlyClosedTabs.Pop();
            AddNewTab(url);
        }
    }

    #endregion

    #region Fullscreen Video Support

    public void SetFullScreen(bool isFullScreen)
    {
        if (_isFullScreen == isFullScreen) return;
        _isFullScreen = isFullScreen;

        if (isFullScreen)
        {
            _previousWindowState = WindowState;
            RowTabStrip.Height = new GridLength(0);
            RowAddressBar.Height = new GridLength(0);
            RowProgressBar.Height = new GridLength(0);
            WindowState = WindowState.Maximized;
        }
        else
        {
            RowTabStrip.Height = new GridLength(42);
            RowAddressBar.Height = GridLength.Auto;
            RowProgressBar.Height = new GridLength(2);
            WindowState = _previousWindowState;
        }
    }

    #endregion

    #region Chrome Extensions Support

    public async Task LoadExtensionsAsync()
    {
        InstalledExtensions.Clear();

        try
        {
            var profile = ActiveTab?.WebView?.CoreWebView2?.Profile;
            if (profile != null)
            {
                var extensions = await profile.GetBrowserExtensionsAsync();
                foreach (var ext in extensions)
                {
                    InstalledExtensions.Add(new BrowserExtensionItem
                    {
                        Id = ext.Id,
                        Name = string.IsNullOrWhiteSpace(ext.Name) ? "Расширение Chrome" : ext.Name,
                        IsEnabled = ext.IsEnabled,
                        CoreExtension = ext
                    });
                }
            }
        }
        catch (Exception ex)
        {
            App.Log($"[Browser Extensions] Error loading extensions: {ex.Message}");
        }

        TxtNoExtensions.Visibility = InstalledExtensions.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void BtnInstallExtension_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dialog = new OpenFileDialog
            {
                Title = "Выберите расширение Chrome (.crx, .zip или manifest.json)",
                Filter = "Расширения Chrome (*.crx;*.zip;manifest.json)|*.crx;*.zip;manifest.json|Все файлы (*.*)|*.*"
            };

            if (dialog.ShowDialog(this) == true)
            {
                string targetDir = ExtensionInstallerService.PrepareExtensionFolder(dialog.FileName);

                var profile = ActiveTab?.WebView?.CoreWebView2?.Profile;
                if (profile == null)
                {
                    MessageBox.Show(this, "Веб-страница еще не загружена. Повторите попытку через секунду.", "Prime Browser", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                await profile.AddBrowserExtensionAsync(targetDir);
                await LoadExtensionsAsync();

                MessageBox.Show(this, "Расширение успешно установлено!", "Prime Browser", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Ошибка установки расширения: {ex.Message}", "Prime Browser", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void ExtensionEnabled_Changed(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is BrowserExtensionItem item && item.CoreExtension != null)
        {
            try
            {
                await item.CoreExtension.EnableAsync(item.IsEnabled);
            }
            catch (Exception ex)
            {
                App.Log($"[Browser Extensions] EnableAsync error: {ex.Message}");
            }
        }
    }

    private async void BtnDeleteExtension_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is BrowserExtensionItem item && item.CoreExtension != null)
        {
            var res = MessageBox.Show(this, $"Удалить расширение '{item.Name}'?", "Prime Browser", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (res == MessageBoxResult.Yes)
            {
                try
                {
                    await item.CoreExtension.RemoveAsync();
                    InstalledExtensions.Remove(item);
                    TxtNoExtensions.Visibility = InstalledExtensions.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, $"Ошибка удаления расширения: {ex.Message}", "Prime Browser", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
        }
    }

    private void BtnOpenWebStore_Click(object sender, RoutedEventArgs e)
    {
        ExtensionsPopup.IsOpen = false;
        AddNewTab("https://chromewebstore.google.com/");
    }

    private void BtnCloseExtensions_Click(object sender, RoutedEventArgs e)
    {
        ExtensionsPopup.IsOpen = false;
    }

    public async void BtnExtensions_Click(object sender, RoutedEventArgs e)
    {
        BrowserSettingsPopup.IsOpen = false;
        ExtensionsPopup.IsOpen = true;
        await LoadExtensionsAsync();
    }

    #endregion

    #region Navigation & Helpers

    private string BuildUrlOrSearch(string input)
    {
        string trimmed = input?.Trim() ?? "";
        if (string.IsNullOrEmpty(trimmed)) return GetDefaultHomeUrl();

        if (trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("file://", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("about:", StringComparison.OrdinalIgnoreCase))
        {
            return trimmed;
        }

        bool isHostPattern = trimmed.Contains(".") && !trimmed.Contains(" ") &&
                             trimmed.IndexOf(".") > 0 && trimmed.IndexOf(".") < trimmed.Length - 1;
        if (isHostPattern)
        {
            return "https://" + trimmed;
        }

        string engine = _vm?.Settings?.SearchEngine ?? "Google";
        string encoded = Uri.EscapeDataString(trimmed);

        return (engine.ToLowerInvariant()) switch
        {
            "яндекс" or "yandex" => $"https://yandex.ru/search/?text={encoded}",
            "duckduckgo" => $"https://duckduckgo.com/?q={encoded}",
            "bing" => $"https://www.bing.com/search?q={encoded}",
            _ => $"https://www.google.com/search?q={encoded}"
        };
    }

    private string GetDefaultHomeUrl()
    {
        string engine = _vm?.Settings?.SearchEngine ?? "Google";
        return (engine.ToLowerInvariant()) switch
        {
            "яндекс" or "yandex" => "https://ya.ru",
            "duckduckgo" => "https://duckduckgo.com",
            "bing" => "https://www.bing.com",
            _ => "https://www.google.com"
        };
    }

    private void NavigateCurrentTab(string input)
    {
        string trimmed = input?.Trim() ?? "";
        if (string.IsNullOrEmpty(trimmed)) return;

        if (trimmed.Equals("about:home", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("about:blank", StringComparison.OrdinalIgnoreCase))
        {
            if (ActiveTab != null)
            {
                ActiveTab.Url = "about:home";
                ActiveTab.Title = "Новая вкладка";
                if (ActiveTab.WebView != null)
                {
                    ActiveTab.WebView.Visibility = Visibility.Collapsed;
                }
            }
            ShowStartScreen(true);
            return;
        }

        string url = BuildUrlOrSearch(trimmed);

        if (ActiveTab == null)
        {
            AddNewTab(url);
            return;
        }

        ActiveTab.Url = url;
        ActiveTab.Title = "Загрузка...";
        ActiveTab.IsLoading = true;

        if (ActiveTab.WebView == null)
        {
            var webView = new WebView2
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                DefaultBackgroundColor = System.Drawing.Color.FromArgb(255, 12, 16, 23)
            };
            ActiveTab.WebView = webView;
            BrowserHostGrid.Children.Add(webView);
            _ = InitializeWebViewForTabAsync(ActiveTab, webView, url);
        }
        else
        {
            try
            {
                ActiveTab.WebView.Source = new Uri(url);
            }
            catch
            {
                ActiveTab.WebView.CoreWebView2?.Navigate(url);
            }
        }

        ShowStartScreen(false);
    }

    private void UpdateNavigationButtonsState()
    {
        try
        {
            if (_btnHome != null) _btnHome.IsEnabled = true;

            if (ActiveTab?.WebView != null)
            {
                bool canBack = ActiveTab.WebView.CanGoBack;
                bool canFwd = ActiveTab.WebView.CanGoForward;
                if (!canBack && ActiveTab.WebView.CoreWebView2 != null)
                    canBack = ActiveTab.WebView.CoreWebView2.CanGoBack;
                if (!canFwd && ActiveTab.WebView.CoreWebView2 != null)
                    canFwd = ActiveTab.WebView.CoreWebView2.CanGoForward;

                _btnBack.IsEnabled = canBack;
                _btnForward.IsEnabled = canFwd;
            }
            else
            {
                _btnBack.IsEnabled = false;
                _btnForward.IsEnabled = false;
            }
        }
        catch
        {
            _btnBack.IsEnabled = false;
            _btnForward.IsEnabled = false;
        }
    }

    private void UpdateSecurityIcon(bool isSecure)
    {
        IconSecurity.Fill = isSecure
            ? (Application.Current?.Resources["AccentBrush"] as Brush ?? Brushes.DodgerBlue)
            : Brushes.OrangeRed;
        IconSecurity.ToolTip = isSecure ? "Безопасное HTTPS подключение" : "Незащищенное HTTP подключение";
    }

    private void UpdateDesktopModeUi()
    {
        if (_txtDesktopModeIcon != null)
        {
            _txtDesktopModeIcon.Text = _isDesktopMode ? "💻" : "📱";
        }
        if (_btnToggleDesktopMode != null)
        {
            _btnToggleDesktopMode.ToolTip = _isDesktopMode
                ? "Режим ПК активен (нажмите для мобильного вида)"
                : "Мобильный режим активен (нажмите для режима ПК)";
        }
    }

    private void AdjustZoom(double delta)
    {
        if (ActiveTab?.WebView != null)
        {
            double newZoom = Math.Clamp(ActiveTab.WebView.ZoomFactor + delta, 0.3, 3.0);
            ActiveTab.WebView.ZoomFactor = newZoom;
        }
    }

    private void ResetZoom()
    {
        if (ActiveTab?.WebView != null)
        {
            ActiveTab.WebView.ZoomFactor = 1.0;
        }
    }

    #endregion

    #region Toolbar Inline Edit Mode Handlers

    public void BtnCustomizeToolbar_Click(object sender, RoutedEventArgs e)
    {
        BrowserSettingsPopup.IsOpen = false;
        ExtensionsPopup.IsOpen = false;

        _isToolbarEditMode = !_isToolbarEditMode;
        if (!_isToolbarEditMode)
        {
            _draggedItem = null;
            RemoveDropIndicator();
            Mouse.Capture(null);
            SaveToolbarConfig();
            Focus();
        }
        UpdateEditModeUi();
    }

    private void BtnExitEditMode_Click(object sender, RoutedEventArgs e)
    {
        _draggedItem = null;
        RemoveDropIndicator();
        Mouse.Capture(null);
        _isToolbarEditMode = false;
        UpdateEditModeUi();
        SaveToolbarConfig();
        Focus();
    }

    private void BtnAddSpacer_Click(object sender, RoutedEventArgs e)
    {
        string newId = "spacer_" + Guid.NewGuid().ToString("N")[..8];
        var newSpacer = new BrowserToolbarItemConfig
        {
            Id = newId,
            Name = "Отступ",
            Icon = "↔",
            Placement = "left",
            Order = ToolbarItems.Count,
            IsVisible = true
        };
        ToolbarItems.Add(newSpacer);
        RebuildToolbar();
        SaveToolbarConfig();
    }

    private void BtnResetToolbar_Click(object sender, RoutedEventArgs e)
    {
        ToolbarItems.Clear();
        foreach (var item in BrowserToolbarItemConfig.GetDefaultToolbar())
        {
            ToolbarItems.Add(item);
        }
        RebuildToolbar();
        SaveToolbarConfig();
    }

    private void UpdateEditModeUi()
    {
        ToolbarEditModeBanner.Visibility = _isToolbarEditMode ? Visibility.Visible : Visibility.Collapsed;
        EditModeOverlay.Visibility = _isToolbarEditMode ? Visibility.Visible : Visibility.Collapsed;

        double tabMinW = _isToolbarEditMode ? 44 : 10;
        TabsToolbarLeftPanel.MinWidth = tabMinW;
        TabsToolbarRightPanel.MinWidth = tabMinW;
        ToolbarLeftPanel.MinWidth = _isToolbarEditMode ? 60 : 0;
        ToolbarRightPanel.MinWidth = _isToolbarEditMode ? 60 : 0;

        if (_isToolbarEditMode)
        {
            if (VerticalTabsPopup.IsOpen) VerticalTabsPopup.IsOpen = false;
            VerticalTabsBar.Visibility = Visibility.Collapsed;
            BrowserHostGrid.Visibility = Visibility.Collapsed;
            StartScreenContainer.Visibility = Visibility.Collapsed;
        }
        else
        {
            bool isVertical = _vm?.Settings?.BrowserVerticalTabs == true;
            ApplyTabsOrientation(isVertical);

            if (_isStartScreenVisible || ActiveTab == null)
            {
                StartScreenContainer.Visibility = Visibility.Visible;
                BrowserHostGrid.Visibility = Visibility.Collapsed;
            }
            else
            {
                BrowserHostGrid.Visibility = Visibility.Visible;
                StartScreenContainer.Visibility = Visibility.Collapsed;
            }
        }

        RebuildToolbar();
    }

    #endregion

    #region Vertical Tabs Expandable Sidebar Handlers

    private bool _isVerticalTabsPopupClosing;
    private bool _isTabContextMenuOpen;

    private void ApplyTabsOrientation(bool isVertical)
    {
        if (isVertical)
        {
            VerticalTabsBar.Visibility = _isToolbarEditMode ? Visibility.Collapsed : Visibility.Visible;
            TabsScrollViewer.Visibility = Visibility.Collapsed;
            BtnNewTab.Visibility = Visibility.Collapsed;
            TxtVerticalTitle.Visibility = Visibility.Visible;
            if (RbVerticalTabs != null) RbVerticalTabs.IsChecked = true;
            if (IconTabOrientation != null) IconTabOrientation.Data = (Geometry)FindResource("IconTabsHorizontal");
            if (BtnToggleTabOrientation != null) BtnToggleTabOrientation.ToolTip = "Переключить на горизонтальные вкладки";
        }
        else
        {
            if (VerticalTabsPopup.IsOpen) VerticalTabsPopup.IsOpen = false;
            VerticalTabsBar.Visibility = Visibility.Collapsed;
            TabsScrollViewer.Visibility = Visibility.Visible;
            BtnNewTab.Visibility = Visibility.Visible;
            TxtVerticalTitle.Visibility = Visibility.Collapsed;
            if (RbHorizontalTabs != null) RbHorizontalTabs.IsChecked = true;
            if (IconTabOrientation != null) IconTabOrientation.Data = (Geometry)FindResource("IconTabsVertical");
            if (BtnToggleTabOrientation != null) BtnToggleTabOrientation.ToolTip = "Переключить на вертикальные вкладки";
        }
    }

    private void BtnToggleTabOrientation_Click(object sender, RoutedEventArgs e)
    {
        bool newVertical = _vm?.Settings?.BrowserVerticalTabs != true;
        if (_vm?.Settings != null)
        {
            _vm.Settings.BrowserVerticalTabs = newVertical;
            _ = _vm.ApplyAndSaveSettingsAsync();
        }
        ApplyTabsOrientation(newVertical);
    }

    private void RbTabOrientation_Checked(object sender, RoutedEventArgs e)
    {
        bool isVertical = RbVerticalTabs.IsChecked == true;
        if (_vm?.Settings != null && _vm.Settings.BrowserVerticalTabs != isVertical)
        {
            _vm.Settings.BrowserVerticalTabs = isVertical;
            _ = _vm.ApplyAndSaveSettingsAsync();
        }
        ApplyTabsOrientation(isVertical);
    }

    private void TabContextMenu_Opened(object sender, RoutedEventArgs e)
    {
        _isTabContextMenuOpen = true;
        _isVerticalTabsPopupClosing = false;
        VerticalTabsPopupBorder.BeginAnimation(WidthProperty, null);
        VerticalTabsPopupBorder.Width = 230;

        if (sender is ContextMenu cm)
        {
            var tab = GetTabFromMenuSender(cm);
            if (tab != null)
            {
                foreach (var item in cm.Items)
                {
                    if (item is MenuItem mi && (string?)mi.Tag == "pin")
                    {
                        mi.Header = tab.IsPinned ? "📌 Открепить вкладку" : "📌 Закрепить вкладку";
                    }
                }
            }
        }
    }

    private void TabContextMenu_Closed(object sender, RoutedEventArgs e)
    {
        _isTabContextMenuOpen = false;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (!_isTabContextMenuOpen && !VerticalTabsPopupBorder.IsMouseOver && !VerticalTabsBar.IsMouseOver)
            {
                CollapseVerticalTabsPopup();
            }
        }), System.Windows.Threading.DispatcherPriority.Input);
    }

    private void VerticalTabsBar_MouseEnter(object sender, MouseEventArgs e)
    {
        if (_isToolbarEditMode || _isTabContextMenuOpen || _vm?.Settings?.BrowserVerticalTabs != true) return;
        OpenVerticalTabsPopup();
    }

    private void OpenVerticalTabsPopup()
    {
        _isVerticalTabsPopupClosing = false;
        VerticalTabsPopupBorder.Height = Math.Max(100, VerticalTabsBar.ActualHeight);
        VerticalTabsPopup.IsOpen = true;

        var anim = new DoubleAnimation
        {
            From = 48,
            To = 230,
            Duration = TimeSpan.FromMilliseconds(180),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        VerticalTabsPopupBorder.BeginAnimation(WidthProperty, anim);
    }

    private void VerticalTabsPopupBorder_MouseEnter(object sender, MouseEventArgs e)
    {
        if (_isVerticalTabsPopupClosing)
        {
            _isVerticalTabsPopupClosing = false;
            var anim = new DoubleAnimation
            {
                To = 230,
                Duration = TimeSpan.FromMilliseconds(150),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            VerticalTabsPopupBorder.BeginAnimation(WidthProperty, anim);
        }
    }

    private void VerticalTabsPopupBorder_MouseLeave(object sender, MouseEventArgs e)
    {
        if (_isTabContextMenuOpen || _isVerticalTabsPopupClosing) return;
        CollapseVerticalTabsPopup();
    }

    private void CollapseVerticalTabsPopup()
    {
        if (_isTabContextMenuOpen || _isVerticalTabsPopupClosing) return;
        _isVerticalTabsPopupClosing = true;

        var anim = new DoubleAnimation
        {
            To = 48,
            Duration = TimeSpan.FromMilliseconds(160),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };
        anim.Completed += (s, ev) =>
        {
            if (_isVerticalTabsPopupClosing && !_isTabContextMenuOpen)
            {
                VerticalTabsPopup.IsOpen = false;
                _isVerticalTabsPopupClosing = false;
            }
        };
        VerticalTabsPopupBorder.BeginAnimation(WidthProperty, anim);
    }

    #endregion

    #region UI Event Handlers

    private void TabButton_Click(object sender, RoutedEventArgs e)
    {
        _tabHoverTimer.Stop();
        CloseTabPreviewPopup();
        if (sender is FrameworkElement fe && fe.Tag is BrowserTabItem tab)
        {
            ActivateTab(tab);
        }
    }

    private void TabButton_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        _tabHoverTimer.Stop();
        CloseTabPreviewPopup();
        if (e.ChangedButton == MouseButton.Middle)
        {
            if (sender is FrameworkElement fe && fe.Tag is BrowserTabItem tab)
            {
                CloseTab(tab);
                e.Handled = true;
            }
        }
    }

    private void TabButton_MouseEnter(object sender, MouseEventArgs e)
    {
        if (_isToolbarEditMode) return;
        var tab = GetTabFromSender(sender);
        if (tab == null) return;

        if (_hoveredTabItem == tab && TabPreviewPopup.IsOpen) return;

        _hoveredTabElement = sender as FrameworkElement;
        _hoveredTabItem = tab;
        _tabHoverTimer.Stop();
        _tabHoverTimer.Start();
    }

    private void TabButton_MouseLeave(object sender, MouseEventArgs e)
    {
        _tabHoverTimer.Stop();
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (_hoveredTabElement == null || !_hoveredTabElement.IsMouseOver)
            {
                CloseTabPreviewPopup();
            }
        }), DispatcherPriority.Input);
    }

    private BrowserTabItem? GetTabFromSender(object sender)
    {
        if (sender is FrameworkElement fe)
        {
            if (fe.Tag is BrowserTabItem tagTab) return tagTab;
            if (fe.DataContext is BrowserTabItem dcTab) return dcTab;
        }
        return null;
    }

    private async void TabHoverTimer_Tick(object? sender, EventArgs e)
    {
        _tabHoverTimer.Stop();
        if (_isToolbarEditMode || _hoveredTabItem == null || _hoveredTabElement == null || !_hoveredTabElement.IsMouseOver)
        {
            return;
        }

        await ShowTabPreviewAsync(_hoveredTabElement, _hoveredTabItem);
    }

    private void CloseTabPreviewPopup()
    {
        _tabHoverTimer.Stop();
        if (TabPreviewPopup.IsOpen)
        {
            TabPreviewPopup.IsOpen = false;
        }
        _hoveredTabItem = null;
        _hoveredTabElement = null;
    }

    private async Task ShowTabPreviewAsync(FrameworkElement target, BrowserTabItem tab)
    {
        TxtPreviewTitle.Text = string.IsNullOrWhiteSpace(tab.Title) ? "Новая вкладка" : tab.Title;
        ImgPreviewFavicon.Source = tab.Favicon;

        string domain = "";
        try
        {
            if (Uri.TryCreate(tab.Url, UriKind.Absolute, out var uri))
            {
                domain = uri.Host;
            }
        }
        catch { }
        TxtPreviewDomain.Text = string.IsNullOrWhiteSpace(domain) ? tab.Url : domain;
        TxtPreviewDomain.Visibility = string.IsNullOrWhiteSpace(TxtPreviewDomain.Text) ? Visibility.Collapsed : Visibility.Visible;

        TxtPreviewActiveStatus.Text = tab.IsActive ? "● Активна" : "○ В фоне";
        TxtPreviewRam.Text = GetTabRamUsage(tab, _sharedEnvironment);

        // Capture or refresh thumbnail preview if available
        if (tab.ThumbnailPreview == null && tab.WebView?.CoreWebView2 != null && !tab.IsLoading)
        {
            await UpdateTabThumbnailAsync(tab);
        }

        if (tab.ThumbnailPreview != null)
        {
            ImgPreviewThumbnail.Source = tab.ThumbnailPreview;
            ImgPreviewThumbnail.Visibility = Visibility.Visible;
            BrdPreviewPlaceholder.Visibility = Visibility.Collapsed;
        }
        else
        {
            ImgPreviewThumbnail.Visibility = Visibility.Collapsed;
            BrdPreviewPlaceholder.Visibility = Visibility.Visible;
        }

        TabPreviewPopup.PlacementTarget = target;
        TabPreviewPopup.Placement = (_vm?.Settings?.BrowserVerticalTabs == true && target != TabsItemsControl)
            ? PlacementMode.Right
            : PlacementMode.Bottom;
        TabPreviewPopup.IsOpen = true;

        AnimateTabPreviewFlyOut();
    }

    private void AnimateTabPreviewFlyOut()
    {
        TabPreviewCard.Opacity = 0;
        var translate = new TranslateTransform(0, -12);
        TabPreviewCard.RenderTransform = translate;

        var fadeAnim = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(240))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        var slideAnim = new DoubleAnimation(-12, 0, TimeSpan.FromMilliseconds(240))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };

        TabPreviewCard.BeginAnimation(UIElement.OpacityProperty, fadeAnim);
        translate.BeginAnimation(TranslateTransform.YProperty, slideAnim);
    }

    private async Task UpdateTabThumbnailAsync(BrowserTabItem tab)
    {
        if (tab.WebView?.CoreWebView2 == null || tab.IsLoading) return;
        try
        {
            using var ms = new MemoryStream();
            await tab.WebView.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, ms);
            ms.Position = 0;
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.StreamSource = ms;
            bmp.EndInit();
            bmp.Freeze();
            tab.ThumbnailPreview = bmp;
        }
        catch { }
    }

    public static string GetTabRamUsage(BrowserTabItem tab, CoreWebView2Environment? env)
    {
        if (tab.Url == "about:home" || tab.Url == "about:blank" || tab.WebView?.CoreWebView2 == null)
        {
            return "ОЗУ: ~16 МБ";
        }

        try
        {
            if (env != null)
            {
                var processInfos = env.GetProcessInfos();
                long totalBytes = 0;
                int renderCount = 0;
                long renderBytes = 0;

                foreach (var pInfo in processInfos)
                {
                    try
                    {
                        using var proc = Process.GetProcessById(pInfo.ProcessId);
                        long ws = proc.WorkingSet64;
                        totalBytes += ws;
                        if (pInfo.Kind == CoreWebView2ProcessKind.Renderer)
                        {
                            renderCount++;
                            renderBytes += ws;
                        }
                    }
                    catch { }
                }

                if (renderBytes > 0 && renderCount > 0)
                {
                    long perTabMb = (renderBytes / renderCount) / (1024 * 1024);
                    return $"ОЗУ: ~{Math.Max(30, perTabMb)} МБ";
                }
                else if (totalBytes > 0)
                {
                    long avgMb = totalBytes / (1024 * 1024);
                    return $"ОЗУ: ~{Math.Max(35, avgMb)} МБ";
                }
            }

            if (tab.WebView.CoreWebView2 != null)
            {
                uint pid = tab.WebView.CoreWebView2.BrowserProcessId;
                using var proc = Process.GetProcessById((int)pid);
                long mb = proc.WorkingSet64 / (1024 * 1024);
                return $"ОЗУ: ~{Math.Max(35, mb)} МБ";
            }
        }
        catch { }

        return "ОЗУ: ~65 МБ";
    }

    private void BtnCloseTab_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is FrameworkElement fe && fe.Tag is BrowserTabItem tab)
        {
            CloseTab(tab);
        }
    }

    private void BtnNewTab_Click(object sender, RoutedEventArgs e)
    {
        AddNewTab();
    }

    private BrowserTabItem? GetTabFromMenuSender(object sender)
    {
        if (sender is FrameworkElement fe)
        {
            if (fe.DataContext is BrowserTabItem tab) return tab;
            if (fe.Tag is BrowserTabItem tagTab) return tagTab;
            if (fe.Parent is ContextMenu cm && cm.PlacementTarget is FrameworkElement pt)
            {
                if (pt.DataContext is BrowserTabItem ptTab) return ptTab;
                if (pt.Tag is BrowserTabItem ptTagTab) return ptTagTab;
            }
        }
        return ActiveTab;
    }

    private void MenuTabNew_Click(object sender, RoutedEventArgs e)
    {
        AddNewTab();
    }

    private void MenuTabReload_Click(object sender, RoutedEventArgs e)
    {
        var tab = GetTabFromMenuSender(sender);
        if (tab?.WebView != null)
        {
            tab.WebView.Reload();
        }
    }

    private void MenuTabDuplicate_Click(object sender, RoutedEventArgs e)
    {
        var tab = GetTabFromMenuSender(sender);
        if (tab != null)
        {
            AddNewTab(tab.Url);
        }
    }

    private void MenuTabPin_Click(object sender, RoutedEventArgs e)
    {
        var tab = GetTabFromMenuSender(sender);
        if (tab == null) return;

        tab.IsPinned = !tab.IsPinned;

        // Reorder tabs: pinned tabs first, then unpinned tabs
        int oldIndex = Tabs.IndexOf(tab);
        if (tab.IsPinned)
        {
            int targetIndex = Tabs.Count(t => t.IsPinned) - 1;
            targetIndex = Math.Clamp(targetIndex, 0, Tabs.Count - 1);
            if (oldIndex != targetIndex)
            {
                Tabs.Move(oldIndex, targetIndex);
            }
        }
        else
        {
            int targetIndex = Tabs.Count(t => t.IsPinned);
            targetIndex = Math.Clamp(targetIndex, 0, Tabs.Count - 1);
            if (oldIndex != targetIndex)
            {
                Tabs.Move(oldIndex, targetIndex);
            }
        }

        ActivateTab(tab);
    }

    private void MenuTabClose_Click(object sender, RoutedEventArgs e)
    {
        var tab = GetTabFromMenuSender(sender);
        if (tab != null)
        {
            CloseTab(tab);
        }
    }

    private void MenuTabCloseOthers_Click(object sender, RoutedEventArgs e)
    {
        var tab = GetTabFromMenuSender(sender);
        if (tab != null)
        {
            var others = Tabs.Where(t => t != tab && !t.IsPinned).ToList();
            foreach (var other in others)
            {
                CloseTab(other);
            }
        }
    }

    private void TabsScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is ScrollViewer sv)
        {
            if (e.Delta < 0) sv.LineRight();
            else sv.LineLeft();
            e.Handled = true;
        }
    }

    private void BrowserHostGrid_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        // Ctrl+MouseWheel for Zoom
        if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            AdjustZoom(e.Delta > 0 ? 0.1 : -0.1);
            e.Handled = true;
        }
    }

    private void BtnBack_Click(object sender, RoutedEventArgs e)
    {
        if (ActiveTab?.WebView != null)
        {
            if (ActiveTab.WebView.CanGoBack)
            {
                ActiveTab.WebView.GoBack();
            }
            else if (ActiveTab.WebView.CoreWebView2?.CanGoBack == true)
            {
                ActiveTab.WebView.CoreWebView2.GoBack();
            }
        }
    }

    private void BtnForward_Click(object sender, RoutedEventArgs e)
    {
        if (ActiveTab?.WebView != null)
        {
            if (ActiveTab.WebView.CanGoForward)
            {
                ActiveTab.WebView.GoForward();
            }
            else if (ActiveTab.WebView.CoreWebView2?.CanGoForward == true)
            {
                ActiveTab.WebView.CoreWebView2.GoForward();
            }
        }
    }

    private void BtnRefresh_Click(object sender, RoutedEventArgs e)
    {
        if (ActiveTab?.WebView != null)
        {
            if (ActiveTab.IsLoading)
            {
                ActiveTab.WebView.Stop();
            }
            else
            {
                ActiveTab.WebView.Reload();
            }
        }
    }

    private void BtnClearAddress_Click(object sender, RoutedEventArgs e)
    {
        TxtAddress.Text = string.Empty;
        TxtAddress.Focus();
    }

    private void BtnGo_Click(object sender, RoutedEventArgs e)
    {
        NavigateCurrentTab(TxtAddress.Text);
    }

    private void TxtAddress_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            NavigateCurrentTab(TxtAddress.Text);
            e.Handled = true;
        }
    }

    private void TxtAddress_GotFocus(object sender, RoutedEventArgs e)
    {
        TxtAddress.SelectAll();
    }

    private void BtnToggleDesktopMode_Click(object sender, RoutedEventArgs e)
    {
        _isDesktopMode = !_isDesktopMode;
        UpdateDesktopModeUi();

        if (ActiveTab != null)
        {
            ApplyTabSettings(ActiveTab);
            ActiveTab.WebView?.Reload();
        }
    }

    private void BtnOpenExternal_Click(object sender, RoutedEventArgs e)
    {
        string url = ActiveTab?.Url ?? TxtAddress.Text;
        if (!string.IsNullOrWhiteSpace(url))
        {
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch { }
        }
    }

    private void BtnBrowserSettings_Click(object sender, RoutedEventArgs e)
    {
        ExtensionsPopup.IsOpen = false;
        BrowserSettingsPopup.PlacementTarget = _btnBrowserSettings;
        BrowserSettingsPopup.IsOpen = !BrowserSettingsPopup.IsOpen;
    }

    private void CmbSearchEngine_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CmbSearchEngine.SelectedItem is string engine && _vm != null)
        {
            _vm.SelectedSearchEngine = engine;
        }
    }

    private void ChkEnableJs_Checked(object sender, RoutedEventArgs e)
    {
        if (_vm?.Settings != null)
        {
            _vm.Settings.BrowserEnableJs = ChkEnableJs.IsChecked == true;
            if (ActiveTab != null) ApplyTabSettings(ActiveTab);
        }
    }

    private void ChkEnableCookies_Checked(object sender, RoutedEventArgs e)
    {
        if (_vm?.Settings != null)
        {
            _vm.Settings.BrowserEnableCookies = ChkEnableCookies.IsChecked == true;
        }
    }

    private void ChkDesktopMode_Checked(object sender, RoutedEventArgs e)
    {
        if (_vm?.Settings != null)
        {
            _vm.Settings.BrowserDesktopMode = ChkDesktopMode.IsChecked == true;
        }
    }

    private async void BtnClearCache_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (ActiveTab?.WebView?.CoreWebView2?.Profile != null)
            {
                await ActiveTab.WebView.CoreWebView2.Profile.ClearBrowsingDataAsync();
            }
            BrowserHistoryService.ClearHistory();
            RefreshRecentSites();
            _vm?.ClearBrowserCacheCommand?.Execute(null);
            MessageBox.Show(this, "Кэш и данные браузера успешно очищены.", "Prime Browser", MessageBoxButton.OK, MessageBoxImage.Information);
            BrowserSettingsPopup.IsOpen = false;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Ошибка очистки кэша: {ex.Message}", "Prime Browser", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    #endregion

    #region Comprehensive Hotkeys Support

    private void BrowserWindow_KeyDown(object sender, KeyEventArgs e)
    {
        bool isCtrl = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;
        bool isShift = (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;
        bool isAlt = (Keyboard.Modifiers & ModifierKeys.Alt) == ModifierKeys.Alt;

        // F11: Toggle Fullscreen
        if (e.Key == Key.F11)
        {
            SetFullScreen(!_isFullScreen);
            e.Handled = true;
            return;
        }

        // Escape: Exit Fullscreen / Exit Toolbar Edit Mode / Stop loading
        if (e.Key == Key.Escape)
        {
            if (_isFullScreen)
            {
                SetFullScreen(false);
                e.Handled = true;
            }
            else if (_isToolbarEditMode)
            {
                BtnExitEditMode_Click(sender, e);
                e.Handled = true;
            }
            else if (ActiveTab?.IsLoading == true)
            {
                ActiveTab.WebView?.Stop();
                e.Handled = true;
            }
            return;
        }

        // Ctrl + Shift + T: Reopen closed tab
        if (isCtrl && isShift && e.Key == Key.T)
        {
            ReopenLastClosedTab();
            e.Handled = true;
            return;
        }

        // Ctrl + T: New Tab
        if (isCtrl && !isShift && e.Key == Key.T)
        {
            AddNewTab();
            e.Handled = true;
            return;
        }

        // Ctrl + W / Ctrl + F4: Close Tab
        if ((isCtrl && e.Key == Key.W) || (isCtrl && e.Key == Key.F4))
        {
            if (ActiveTab != null) CloseTab(ActiveTab);
            e.Handled = true;
            return;
        }

        // Ctrl + Tab / Ctrl + PageDown: Next Tab
        if ((isCtrl && !isShift && e.Key == Key.Tab) || (isCtrl && e.Key == Key.PageDown))
        {
            if (Tabs.Count > 1 && ActiveTab != null)
            {
                int curIdx = Tabs.IndexOf(ActiveTab);
                int nextIdx = (curIdx + 1) % Tabs.Count;
                ActivateTab(Tabs[nextIdx]);
            }
            e.Handled = true;
            return;
        }

        // Ctrl + Shift + Tab / Ctrl + PageUp: Previous Tab
        if ((isCtrl && isShift && e.Key == Key.Tab) || (isCtrl && e.Key == Key.PageUp))
        {
            if (Tabs.Count > 1 && ActiveTab != null)
            {
                int curIdx = Tabs.IndexOf(ActiveTab);
                int prevIdx = (curIdx - 1 + Tabs.Count) % Tabs.Count;
                ActivateTab(Tabs[prevIdx]);
            }
            e.Handled = true;
            return;
        }

        // Ctrl + 1..8: Switch to tab index
        if (isCtrl && e.Key >= Key.D1 && e.Key <= Key.D8)
        {
            int targetIdx = e.Key - Key.D1;
            if (targetIdx < Tabs.Count)
            {
                ActivateTab(Tabs[targetIdx]);
            }
            e.Handled = true;
            return;
        }

        // Ctrl + 9: Switch to last tab
        if (isCtrl && e.Key == Key.D9)
        {
            if (Tabs.Count > 0)
            {
                ActivateTab(Tabs[Tabs.Count - 1]);
            }
            e.Handled = true;
            return;
        }

        // Ctrl + L / Alt + D / F6: Focus address bar
        if ((isCtrl && e.Key == Key.L) || (isAlt && e.Key == Key.D) || e.Key == Key.F6)
        {
            TxtAddress.Focus();
            TxtAddress.SelectAll();
            e.Handled = true;
            return;
        }

        // F5 / Ctrl + R: Refresh
        if (e.Key == Key.F5 || (isCtrl && !isShift && e.Key == Key.R))
        {
            ActiveTab?.WebView?.Reload();
            e.Handled = true;
            return;
        }

        // Ctrl + F5 / Ctrl + Shift + R: Hard refresh
        if ((isCtrl && e.Key == Key.F5) || (isCtrl && isShift && e.Key == Key.R))
        {
            if (ActiveTab?.WebView?.CoreWebView2 != null)
            {
                ActiveTab.WebView.CoreWebView2.Reload();
            }
            e.Handled = true;
            return;
        }

        // Alt + Left: Back
        if (isAlt && e.Key == Key.Left)
        {
            if (ActiveTab?.WebView?.CanGoBack == true) ActiveTab.WebView.GoBack();
            e.Handled = true;
            return;
        }

        // Alt + Right: Forward
        if (isAlt && e.Key == Key.Right)
        {
            if (ActiveTab?.WebView?.CanGoForward == true) ActiveTab.WebView.GoForward();
            e.Handled = true;
            return;
        }

        // Alt + Home: Home Page (Start Screen)
        if (isAlt && e.Key == Key.Home)
        {
            NavigateCurrentTab("about:home");
            e.Handled = true;
            return;
        }

        // Zoom In: Ctrl + Plus / Ctrl + OemPlus / Ctrl + Add
        if (isCtrl && (e.Key == Key.OemPlus || e.Key == Key.Add))
        {
            AdjustZoom(0.1);
            e.Handled = true;
            return;
        }

        // Zoom Out: Ctrl + Minus / Ctrl + OemMinus / Ctrl + Subtract
        if (isCtrl && (e.Key == Key.OemMinus || e.Key == Key.Subtract))
        {
            AdjustZoom(-0.1);
            e.Handled = true;
            return;
        }

        // Reset Zoom: Ctrl + 0 / Ctrl + NumPad0
        if (isCtrl && (e.Key == Key.D0 || e.Key == Key.NumPad0))
        {
            ResetZoom();
            e.Handled = true;
            return;
        }

        // Ctrl + Shift + E or Ctrl + J: Open Chrome Extensions
        if ((isCtrl && isShift && e.Key == Key.E) || (isCtrl && e.Key == Key.J))
        {
            BtnExtensions_Click(sender, e);
            e.Handled = true;
            return;
        }

        // Ctrl + P: Print Page
        if (isCtrl && e.Key == Key.P)
        {
            try
            {
                ActiveTab?.WebView?.CoreWebView2?.ShowPrintUI(CoreWebView2PrintDialogKind.Browser);
            }
            catch { }
            e.Handled = true;
            return;
        }

        // Ctrl + H / Ctrl + Shift + Delete: Clear Data
        if ((isCtrl && e.Key == Key.H) || (isCtrl && isShift && e.Key == Key.Delete))
        {
            BtnClearCache_Click(sender, e);
            e.Handled = true;
            return;
        }
    }

    private void BtnMinimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void BtnMaximize_Click(object sender, RoutedEventArgs e) =>
        WindowState = (WindowState == WindowState.Maximized) ? WindowState.Normal : WindowState.Maximized;
    private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();

    #endregion

    #region Start Screen & History & Brand Menu

    private bool _isStartScreenVisible = false;

    private void ShowStartScreen(bool show)
    {
        _isStartScreenVisible = show;
        StartScreenContainer.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        BrowserHostGrid.Visibility = show ? Visibility.Collapsed : Visibility.Visible;

        if (show)
        {
            TxtAddress.Text = "";
            UpdateSecurityIcon(true);
            if (_btnBack != null) _btnBack.IsEnabled = false;
            if (_btnForward != null) _btnForward.IsEnabled = false;
            if (_btnRefresh != null) _btnRefresh.IsEnabled = false;

            RefreshRecentSites();
            UpdateDefaultBrowserRadioButtons();
            UpdateSearchEngineChecks();

            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, new Action(() =>
            {
                StartSearchTextBox.Focus();
            }));
        }
    }

    private void RefreshRecentSites()
    {
        var sites = BrowserHistoryService.LoadRecentSites();
        if (sites.Count == 0)
        {
            BrdNoRecentSites.Visibility = Visibility.Visible;
            RecentSitesItemsControl.Visibility = Visibility.Collapsed;
        }
        else
        {
            BrdNoRecentSites.Visibility = Visibility.Collapsed;
            RecentSitesItemsControl.Visibility = Visibility.Visible;
            RecentSitesItemsControl.ItemsSource = sites;
        }
    }

    private void BtnClearRecentSites_Click(object sender, RoutedEventArgs e)
    {
        BrowserHistoryService.ClearHistory();
        RefreshRecentSites();
    }

    private void RecentSiteTile_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is RecentBrowserSite site)
        {
            if (!string.IsNullOrWhiteSpace(site.Url))
            {
                NavigateCurrentTab(site.Url);
            }
        }
    }

    private void BtnGoToBrowserSettings_Click(object sender, RoutedEventArgs e)
    {
        if (_vm != null)
        {
            WindowManager.OpenSettings(_vm, 3);
        }
    }

    private void SubmitStartSearch()
    {
        string query = StartSearchTextBox.Text?.Trim() ?? "";
        if (!string.IsNullOrWhiteSpace(query))
        {
            StartSearchTextBox.Text = "";
            NavigateCurrentTab(query);
        }
    }

    private void StartSearchTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            SubmitStartSearch();
            e.Handled = true;
        }
    }

    private void BtnStartSearchSubmit_Click(object sender, RoutedEventArgs e)
    {
        SubmitStartSearch();
    }

    private void StartSearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        StartSearchPlaceholder.Visibility = string.IsNullOrEmpty(StartSearchTextBox.Text)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void BtnStartSearchMenu_Click(object sender, RoutedEventArgs e)
    {
        UpdateDefaultBrowserRadioButtons();
        UpdateSearchEngineChecks();
        TxtStartSavedNotification.Visibility = Visibility.Collapsed;
        StartBrowserMenuPopup.IsOpen = !StartBrowserMenuPopup.IsOpen;
    }

    private void UpdateDefaultBrowserRadioButtons()
    {
        bool useInternal = _vm?.UseInternalBrowser ?? true;
        RbStartBrowserInternal.IsChecked = useInternal;
        RbStartBrowserSystem.IsChecked = !useInternal;
    }

    private void UpdateSearchEngineChecks()
    {
        string engine = _vm?.SelectedSearchEngine ?? "Google";
        TxtCurrentEngineBadge.Text = engine;
        TxtCheckGoogle.Visibility = (engine == "Google") ? Visibility.Visible : Visibility.Collapsed;
        TxtCheckYandex.Visibility = (engine == "Яндекс") ? Visibility.Visible : Visibility.Collapsed;
        TxtCheckDuckDuckGo.Visibility = (engine == "DuckDuckGo") ? Visibility.Visible : Visibility.Collapsed;
        TxtCheckBing.Visibility = (engine == "Bing") ? Visibility.Visible : Visibility.Collapsed;
    }

    private void RbStartBrowserInternal_Click(object sender, RoutedEventArgs e)
    {
        if (_vm != null)
        {
            _vm.UseInternalBrowser = true;
            _ = _vm.ApplyAndSaveSettingsAsync();
            TxtStartSavedNotification.Visibility = Visibility.Visible;
        }
    }

    private void RbStartBrowserSystem_Click(object sender, RoutedEventArgs e)
    {
        if (_vm != null)
        {
            _vm.UseInternalBrowser = false;
            _ = _vm.ApplyAndSaveSettingsAsync();
            TxtStartSavedNotification.Visibility = Visibility.Visible;
        }
    }

    private void StartSearchEngineItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is string engine && _vm != null)
        {
            _vm.SelectedSearchEngine = engine;
            if (_vm.Settings != null) _vm.Settings.SearchEngine = engine;
            CmbSearchEngine.SelectedItem = engine;
            _ = _vm.ApplyAndSaveSettingsAsync();
            UpdateSearchEngineChecks();
            TxtStartSavedNotification.Visibility = Visibility.Visible;
        }
    }

    #endregion

    #region Hidden Toolbar Buttons Management

    private void UpdateHiddenButtonsInSettingsPopup()
    {
        HiddenToolbarButtons.Clear();
        foreach (var item in ToolbarItems.Where(i => !i.IsVisible && i.Id != "settings" && !i.Id.StartsWith("spacer")))
        {
            string title = item.Id switch
            {
                "home" => "Главная страница поиска",
                "back" => "Назад",
                "forward" => "Вперед",
                "refresh" => "Обновить страницу",
                "desktop_mode" => "Режим ПК / Мобильный",
                "extensions" => "Расширения Chrome",
                "open_external" or "external" => "Открыть в системном браузере",
                "customize" => "Настройка кнопок (Карандаш)",
                _ => item.Id
            };
            HiddenToolbarButtons.Add(new HiddenToolbarItemViewModel
            {
                Id = item.Id,
                Title = title,
                Config = item
            });
        }

        BrdHiddenButtonsSection.Visibility = HiddenToolbarButtons.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void BtnRestoreHiddenButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is HiddenToolbarItemViewModel vm)
        {
            vm.Config.IsVisible = true;
            RebuildToolbar();
            SaveToolbarConfig();
            UpdateHiddenButtonsInSettingsPopup();
        }
    }

    private void BtnExecuteHiddenButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is HiddenToolbarItemViewModel vm)
        {
            BrowserSettingsPopup.IsOpen = false;
            switch (vm.Id)
            {
                case "home":
                    NavigateCurrentTab("about:home");
                    break;
                case "back":
                    BtnBack_Click(_btnBack, new RoutedEventArgs());
                    break;
                case "forward":
                    BtnForward_Click(_btnForward, new RoutedEventArgs());
                    break;
                case "refresh":
                    BtnRefresh_Click(_btnRefresh, new RoutedEventArgs());
                    break;
                case "desktop_mode":
                    BtnToggleDesktopMode_Click(_btnToggleDesktopMode, new RoutedEventArgs());
                    break;
                case "extensions":
                    BtnExtensions_Click(_btnExtensions, new RoutedEventArgs());
                    break;
                case "open_external":
                    BtnOpenExternal_Click(_btnOpenExternal, new RoutedEventArgs());
                    break;
                case "customize":
                    BtnCustomizeToolbar_Click(_btnCustomizeToolbar, new RoutedEventArgs());
                    break;
            }
        }
    }

    #endregion

    #region Tabs Animation & Counter & Address Bar Row Placement

    private void AnimateTabsFlyUp()
    {
        // Each tab button animates individually via its Loaded EventTrigger in XAML!
    }

    private void UpdateTabsCounter()
    {
        try
        {
            int count = Tabs.Count;
            if (TxtTabsCount != null) TxtTabsCount.Text = count.ToString();
            if (BtnTabsCounter != null)
            {
                BtnTabsCounter.Visibility = count >= 2 ? Visibility.Visible : Visibility.Collapsed;
            }
            if (TabsListDropdownItemsControl != null)
            {
                TabsListDropdownItemsControl.ItemsSource = null;
                TabsListDropdownItemsControl.ItemsSource = Tabs;
            }
        }
        catch { }
    }

    private void BtnTabsCounter_Click(object sender, RoutedEventArgs e)
    {
        UpdateTabsCounter();
        TabsDropdownPopup.IsOpen = !TabsDropdownPopup.IsOpen;
    }

    private void BtnTabsCounter_MouseEnter(object sender, MouseEventArgs e)
    {
        UpdateTabsCounter();
        TabsDropdownPopup.IsOpen = true;
    }

    private void BtnCloseTabFromDropdown_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is BrowserTabItem tab)
        {
            CloseTab(tab);
            UpdateTabsCounter();
        }
    }

    private void TabsDropdownItem_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is BrowserTabItem tab)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                ActivateTab(tab);
                TabsDropdownPopup.IsOpen = false;
                e.Handled = true;
            }
            else if (e.ChangedButton == MouseButton.Middle)
            {
                CloseTab(tab);
                UpdateTabsCounter();
                e.Handled = true;
            }
        }
    }



    #endregion

}