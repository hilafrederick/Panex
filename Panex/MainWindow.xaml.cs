// File: \MainWindow.xaml.cs
using CommunityToolkit.WinUI.UI.Controls;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Animation;
using Panex.ViewModels;
using ScottPlot.WinUI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Windows.Graphics;
using Windows.UI;
using WinUIEx;

namespace Panex
{
    public sealed partial class MainWindow : WindowEx
    {
        private bool _drawerOpen = false;
        private double _drawerWidth = 820;
        public MainViewModel ViewModel { get; }

        // Class-level fields
        private Microsoft.UI.Dispatching.DispatcherQueueTimer _repaintTimer;
        private bool _isHoverGlobalEnabled = false;
        private Dictionary<ScottPlot.WinUI.WinUIPlot, ScottPlot.Plottables.VerticalLine> _plotCrosshairs = new();
        private HashSet<ScottPlot.WinUI.WinUIPlot> _initializedPlots = new();

        public MainWindow()
        {
            this.InitializeComponent();

            // 1. Clean, native AppWindow sizing using WinUIEx
            this.SetWindowSize(1366, 768);
            this.MinWidth = 1280;
            this.MinHeight = 720;
            this.CenterOnScreen();

            // Set Window and Taskbar Icon
            var iconPath = System.IO.Path.Combine(System.AppContext.BaseDirectory, "Assets", "app.ico");
            if (System.IO.File.Exists(iconPath))
            {
                this.AppWindow.SetIcon(iconPath);
            }

            // Set TitleBar Icon for unpackaged execution
            var logoPath = System.IO.Path.Combine(System.AppContext.BaseDirectory, "Assets", "logo.png");
            if (System.IO.File.Exists(logoPath) && TitleBarIcon != null)
            {
                TitleBarIcon.Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(logoPath));
            }

            // 2. Style TitleBar & Caption Buttons
            ConfigureTitleBar();

            // 3. Initialize the Debounce Timer for Chart Rendering
            _repaintTimer = this.DispatcherQueue.CreateTimer();
            _repaintTimer.Interval = TimeSpan.FromMilliseconds(30); // Restricts updates to ~30 FPS
            _repaintTimer.Tick += (s, e) =>
            {
                _repaintTimer.Stop(); // Stop timer so it only fires once per burst
                UpdatePlots();        // Render the active chart
            };

            // 4. VIEWMODEL SUBSCRIPTIONS
            ViewModel = (MainViewModel)RootGrid.DataContext;

            LoadThemePreference();

            this.RootGrid.Loaded += async (s, e) =>
            {
                await ViewModel.InitializeDataAsync();
            };

            // Debounce the Repaint Request
            ViewModel.RequestRepaint += (s, e) =>
            {
                _repaintTimer.Stop();
                _repaintTimer.Start();
            };

            ViewModel.MaterialCreated += (s, e) => CloseDrawer();
            ViewModel.ShowAlert += ShowAlertDialog;
            ViewModel.RequestConfirmation += ShowConfirmationDialog;

            // Disable material selection highlighting when in Compare Mode
            ViewModel.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(ViewModel.IsCompareMode))
                {
                    MaterialListView.SelectionMode = ViewModel.IsCompareMode
                        ? ListViewSelectionMode.None
                        : ListViewSelectionMode.Single;

                    // Cancel the deferred timer and swap the plot data immediately
                    // so the plot doesn't redraw the old partial curves during its resize pass.
                    _repaintTimer.Stop();
                    UpdatePlots();
                }
            };
        }

        // --- TITLE BAR STYLING & DRAG REGION METHODS ---

        private void ConfigureTitleBar()
        {
            this.ExtendsContentIntoTitleBar = true;
            this.SetTitleBar(TitleBarDragRegion);

            if (AppWindowTitleBar.IsCustomizationSupported())
            {
                this.AppWindow.TitleBar.ButtonBackgroundColor = Microsoft.UI.Colors.Transparent;
                this.AppWindow.TitleBar.ButtonInactiveBackgroundColor = Microsoft.UI.Colors.Transparent;

                var currentTheme = Application.Current.RequestedTheme == ApplicationTheme.Dark
                    ? ElementTheme.Dark
                    : ElementTheme.Light;
                UpdateTitleBarTheme(currentTheme);
            }
        }

        private void AppTitleBar_Loaded(object sender, RoutedEventArgs e) => UpdateTitleBarDragRectangles();

        private void AppTitleBar_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateTitleBarDragRectangles();

        private void UpdateTitleBarDragRectangles()
        {
            if (!AppWindowTitleBar.IsCustomizationSupported() || AppTitleBar.XamlRoot == null) return;

            try
            {
                double scale = AppTitleBar.XamlRoot.RasterizationScale;
                int rightInset = this.AppWindow.TitleBar.RightInset;
                if (rightInset > 0)
                {
                    RightPaddingColumn.Width = new GridLength(rightInset / scale);
                }

                var dragTransform = TitleBarDragRegion.TransformToVisual(null);
                var dragBounds = dragTransform.TransformBounds(new Windows.Foundation.Rect(0, 0, TitleBarDragRegion.ActualWidth, TitleBarDragRegion.ActualHeight));

                var dragRect = new RectInt32(
                    (int)Math.Round(dragBounds.X * scale),
                    (int)Math.Round(dragBounds.Y * scale),
                    (int)Math.Max(0, Math.Round(dragBounds.Width * scale)),
                    (int)Math.Max(0, Math.Round(dragBounds.Height * scale))
                );

                this.AppWindow.TitleBar.SetDragRectangles(new[] { dragRect });

                var nonClientInputSrc = InputNonClientPointerSource.GetForWindowId(this.AppWindow.Id);
                var utilTransform = TitleBarUtilitiesPanel.TransformToVisual(null);
                var utilBounds = utilTransform.TransformBounds(new Windows.Foundation.Rect(0, 0, TitleBarUtilitiesPanel.ActualWidth, TitleBarUtilitiesPanel.ActualHeight));

                var passthroughRect = new RectInt32(
                    (int)Math.Round(utilBounds.X * scale),
                    (int)Math.Round(utilBounds.Y * scale),
                    (int)Math.Max(0, Math.Round(utilBounds.Width * scale)),
                    (int)Math.Max(0, Math.Round(utilBounds.Height * scale))
                );

                nonClientInputSrc.SetRegionRects(NonClientRegionKind.Passthrough, new[] { passthroughRect });
            }
            catch
            {
                // Safely ignore if window handle / non-client region is not ready yet
            }
        }

        private void UpdateTitleBarTheme(ElementTheme theme)
        {
            if (AppWindowTitleBar.IsCustomizationSupported())
            {
                var titleBar = this.AppWindow.TitleBar;

                if (theme == ElementTheme.Dark)
                {
                    titleBar.ButtonForegroundColor = Microsoft.UI.Colors.White;
                    titleBar.ButtonHoverForegroundColor = Microsoft.UI.Colors.White;
                    titleBar.ButtonHoverBackgroundColor = Color.FromArgb(25, 255, 255, 255);
                    titleBar.ButtonPressedForegroundColor = Microsoft.UI.Colors.White;
                    titleBar.ButtonPressedBackgroundColor = Color.FromArgb(51, 255, 255, 255);
                }
                else
                {
                    titleBar.ButtonForegroundColor = Microsoft.UI.Colors.Black;
                    titleBar.ButtonHoverForegroundColor = Microsoft.UI.Colors.Black;
                    titleBar.ButtonHoverBackgroundColor = Color.FromArgb(25, 0, 0, 0);
                    titleBar.ButtonPressedForegroundColor = Microsoft.UI.Colors.Black;
                    titleBar.ButtonPressedBackgroundColor = Color.FromArgb(51, 0, 0, 0);
                }
            }

            if (ThemeToggleIcon != null && ThemeToggleText != null)
            {
                if (theme == ElementTheme.Dark)
                {
                    ThemeToggleIcon.Glyph = "\uE708"; // Moon icon (Dark active)
                    ThemeToggleText.Text = "Dark";
                }
                else
                {
                    ThemeToggleIcon.Glyph = "\uE706"; // Sun icon (Light active)
                    ThemeToggleText.Text = "Light";
                }
            }
        }

        // --- INTERACTION & DRAWER METHODS ---

        private void RootGrid_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Escape && _drawerOpen)
            {
                CloseDrawer();
                e.Handled = true;
            }
        }

        private void MenuLibrary_Click(object sender, RoutedEventArgs e)
        {
            if (sender == MenuEpdl)
            {
                MenuEpdl.IsChecked = true;
                MenuXcom.IsChecked = false;
                if (ActiveDatabaseText != null) ActiveDatabaseText.Text = "LLNL EPDL (EPICS2025)";
                ViewModel.ChangeDatabaseLibrary("EPDL");
            }
            else if (sender == MenuXcom)
            {
                MenuXcom.IsChecked = true;
                MenuEpdl.IsChecked = false;
                if (ActiveDatabaseText != null) ActiveDatabaseText.Text = "NIST XCOM";
                ViewModel.ChangeDatabaseLibrary("XCOM");
            }
        }

        private void OpenDrawerNewButton_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.IsLoadMode = false;
            OpenDrawer();
        }

        private void OpenDrawerLoadButton_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.IsLoadMode = true;
            OpenDrawer();
        }

        private void CloseDrawerButton_Click(object sender, RoutedEventArgs e) => CloseDrawer();

        private void DrawerScrim_PointerPressed(object sender, PointerRoutedEventArgs e) => CloseDrawer();

        private void CreateMaterial_Click(object sender, RoutedEventArgs e) => CloseDrawer();

        private void OpenDrawer()
        {
            if (_drawerOpen) return;
            _drawerOpen = true;
            DrawerScrim.IsHitTestVisible = true;

            if (OpenDrawerLoadWidthAnim != null) OpenDrawerLoadWidthAnim.To = _drawerWidth;
            if (OpenDrawerNewWidthAnim != null) OpenDrawerNewWidthAnim.To = _drawerWidth;

            if (RootGrid.DataContext is MainViewModel vm)
            {
                string storyboardName = vm.IsLoadMode ? "OpenDrawerLoad" : "OpenDrawerNew";

                if (RootGrid.Resources[storyboardName] is Storyboard sb)
                {
                    sb.Begin();
                }
            }
        }

        private void OpenDrawerStoryboard_Completed(object sender, object e)
        {
            if (!_drawerOpen) return;

            CreateDrawer.Width = _drawerWidth;
            DrawerContent.Opacity = 1;
            DrawerScrim.Opacity = 0.5;

            if (sender is Storyboard sb)
            {
                sb.Stop();
            }
        }

        private void CloseDrawer()
        {
            if (!_drawerOpen) return;
            _drawerOpen = false;
            DrawerScrim.IsHitTestVisible = false;

            if (RootGrid.Resources["CloseDrawer"] is Storyboard sb)
            {
                sb.Begin();
            }
        }

        private void CloseDrawerStoryboard_Completed(object sender, object e)
        {
            if (_drawerOpen) return;

            CreateDrawer.Width = 0;
            DrawerContent.Opacity = 0;
            DrawerScrim.Opacity = 0;

            if (sender is Storyboard sb)
            {
                sb.Stop();
            }
        }

        private void LibraryDataGrid_Sorting(object sender, CommunityToolkit.WinUI.UI.Controls.DataGridColumnEventArgs e)
        {
            if (RootGrid.DataContext is MainViewModel vm)
            {
                bool ascending = true;
                if (e.Column.SortDirection == null || e.Column.SortDirection == CommunityToolkit.WinUI.UI.Controls.DataGridSortDirection.Descending)
                {
                    ascending = true;
                    e.Column.SortDirection = CommunityToolkit.WinUI.UI.Controls.DataGridSortDirection.Ascending;
                }
                else
                {
                    ascending = false;
                    e.Column.SortDirection = CommunityToolkit.WinUI.UI.Controls.DataGridSortDirection.Descending;
                }

                foreach (var column in ((CommunityToolkit.WinUI.UI.Controls.DataGrid)sender).Columns)
                {
                    if (column != e.Column) column.SortDirection = null;
                }

                vm.SortLibrary(e.Column.Tag.ToString(), ascending);
            }
        }

        private void ListView_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdatePlots();

        private async void OnTabChanged(object sender, SelectionChangedEventArgs e)
        {
            bool isTabChange = e.AddedItems.FirstOrDefault() is TabViewItem ||
                               e.RemovedItems.FirstOrDefault() is TabViewItem;

            if (!isTabChange) return;

            HoverTooltip.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;

            // 1. Hide all plots instantly to prevent ghosting of stale data
            CrossSectionPlot.Opacity = 0;
            MacPlot.Opacity = 0;
            LacPlot.Opacity = 0;
            DepthPlot.Opacity = 0;
            ZeffPlot.Opacity = 0;
            BuildupPlot.Opacity = 0;
            ZeqPlot.Opacity = 0;

            await Task.Delay(50);

            UpdatePlots();
            ViewModel.UpdateTablesCommand.Execute(null);

            // 2. Restore visibility now that the fresh frame is ready
            CrossSectionPlot.Opacity = 1;
            MacPlot.Opacity = 1;
            LacPlot.Opacity = 1;
            DepthPlot.Opacity = 1;
            ZeffPlot.Opacity = 1;
            BuildupPlot.Opacity = 1;
            ZeqPlot.Opacity = 1;
        }

        private void MaterialListPanel_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (e.OriginalSource is FrameworkElement el)
            {
                if (el.DataContext is not Models.Material)
                {
                    ViewModel.SelectedMaterial = null;
                }
            }
        }

        private void Plot_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is ScottPlot.WinUI.WinUIPlot plot)
            {
                if (_initializedPlots.Contains(plot)) return;
                _initializedPlots.Add(plot);

                plot.UserInputProcessor.Disable();
                plot.Menu.Clear();

                var menu = new MenuFlyout();
                var saveItem = new MenuFlyoutItem
                {
                    Text = "Export Chart",
                    Icon = new FontIcon { Glyph = "\uE74E" }
                };
                saveItem.Click += SaveSvg_Click;

                var settingsItem = new MenuFlyoutItem
                {
                    Text = "Chart Settings",
                    Icon = new FontIcon { Glyph = "\uE713" }
                };
                settingsItem.Click += (s, args) =>
                {
                    HiddenSettingsButton.Flyout.ShowAt(HiddenSettingsButton, new Microsoft.UI.Xaml.Controls.Primitives.FlyoutShowOptions
                    {
                        Placement = Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.BottomEdgeAlignedRight
                    });
                };

                menu.Items.Add(saveItem);
                menu.Items.Add(settingsItem);
                plot.ContextFlyout = menu;

                plot.PointerPressed += Plot_PointerPressed;
                plot.PointerMoved += Plot_PointerMoved;
                plot.PointerExited += Plot_PointerExited;

                UpdateSpecificPlot(plot);
            }
        }

        private void CrosshairToggleButton_Click(object sender, RoutedEventArgs e)
        {
            _isHoverGlobalEnabled = CrosshairToggleButton.IsChecked == true;
            if (!_isHoverGlobalEnabled)
            {
                HideCrosshairAndTooltip();
            }
        }

        private void HideCrosshairAndTooltip(WinUIPlot? specificPlot = null)
        {
            HoverTooltip.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;

            if (specificPlot != null)
            {
                if (_plotCrosshairs.TryGetValue(specificPlot, out var vLine))
                {
                    vLine.IsVisible = false;
                    specificPlot.Refresh();
                }
                return;
            }

            foreach (var kvp in _plotCrosshairs)
            {
                kvp.Value.IsVisible = false;
                kvp.Key.Refresh();
            }
        }

        private void Plot_PointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if (sender is ScottPlot.WinUI.WinUIPlot plotControl)
            {
                var props = e.GetCurrentPoint(plotControl).Properties;
                if (props.IsLeftButtonPressed)
                {
                    _isHoverGlobalEnabled = !_isHoverGlobalEnabled;
                    if (CrosshairToggleButton != null)
                    {
                        CrosshairToggleButton.IsChecked = _isHoverGlobalEnabled;
                    }

                    if (!_isHoverGlobalEnabled)
                    {
                        HideCrosshairAndTooltip(plotControl);
                    }
                    else
                    {
                        Plot_PointerMoved(sender, e);
                    }
                }
            }
        }

        private void Plot_PointerExited(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            HoverTooltip.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
            if (sender is ScottPlot.WinUI.WinUIPlot plotControl)
            {
                if (_plotCrosshairs.TryGetValue(plotControl, out var vLine))
                {
                    vLine.IsVisible = false;
                    plotControl.Refresh();
                }
            }
        }

        private void Plot_PointerMoved(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if (sender is ScottPlot.WinUI.WinUIPlot plotControl && (ViewModel.SelectedMaterial != null || ViewModel.IsCompareMode))
            {
                if (!_isHoverGlobalEnabled) return;

                var position = e.GetCurrentPoint(plotControl).Position;

                // Apply Windows Display Scaling (DPI) so ScottPlot calculates the exact physical pixel
                double scale = plotControl.XamlRoot.RasterizationScale;
                ScottPlot.Pixel mousePixel = new ScottPlot.Pixel(position.X * scale, position.Y * scale);
                ScottPlot.Coordinates mouseCoords = plotControl.Plot.GetCoordinates(mousePixel);

                PlotSettings? settings = null;
                if (plotControl == CrossSectionPlot) settings = ViewModel.CrossSectionSettings;
                else if (plotControl == MacPlot) settings = ViewModel.MacSettings;
                else if (plotControl == LacPlot) settings = ViewModel.LacSettings;
                else if (plotControl == DepthPlot) settings = ViewModel.DepthsSettings;
                else if (plotControl == ZeffPlot) settings = ViewModel.ZeffSettings;
                else if (plotControl == ZeqPlot) settings = ViewModel.ZeqSettings;
                else if (plotControl == BuildupPlot) settings = ViewModel.BuildupSettings;

                if (settings == null || !ViewModel.HoverDataMap.TryGetValue(settings, out var curves) || curves.Count == 0)
                {
                    HoverTooltip.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
                    return;
                }

                if (mouseCoords.X < plotControl.Plot.Axes.Bottom.Min || mouseCoords.X > plotControl.Plot.Axes.Bottom.Max)
                {
                    HoverTooltip.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
                    return;
                }

                double targetX = mouseCoords.X;
                double closestX = targetX;
                var refCurve = curves[0];

                // 1. O(log N) Binary Search for the closest X coordinate
                int index = Array.BinarySearch(refCurve.Xs, targetX);
                if (index < 0)
                {
                    index = ~index; // Bitwise complement gets the index of the first larger element
                    if (index == 0) closestX = refCurve.Xs[0];
                    else if (index >= refCurve.Xs.Length) closestX = refCurve.Xs[refCurve.Xs.Length - 1];
                    else
                    {
                        double leftDiff = targetX - refCurve.Xs[index - 1];
                        double rightDiff = refCurve.Xs[index] - targetX;
                        closestX = leftDiff < rightDiff ? refCurve.Xs[index - 1] : refCurve.Xs[index];
                    }
                }
                else
                {
                    closestX = refCurve.Xs[index];
                }

                var hoverData = new List<HoverItem>();
                foreach (var curve in curves)
                {
                    // 2. O(log N) Binary Search to find the exact Y value for the closest X on THIS curve
                    int curveIndex = Array.BinarySearch(curve.Xs, closestX);

                    // Fallback in case floating point precision causes a miss, find the nearest
                    if (curveIndex < 0)
                    {
                        curveIndex = ~curveIndex;
                        if (curveIndex >= curve.Xs.Length) curveIndex = curve.Xs.Length - 1;
                        else if (curveIndex > 0)
                        {
                            double leftDiff = Math.Abs(curve.Xs[curveIndex - 1] - closestX);
                            double rightDiff = Math.Abs(curve.Xs[curveIndex] - closestX);
                            if (leftDiff < rightDiff) curveIndex--;
                        }
                    }

                    double yVal = curve.Ys[curveIndex];
                    double displayY = settings.IsYLog ? Math.Pow(10, yVal) : yVal;

                    hoverData.Add(new HoverItem
                    {
                        Label = curve.Label,
                        ValueString = displayY.ToString("E4"),
                        ColorBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(curve.UiColor)
                    });
                }

                double displayX = settings.IsXLog ? Math.Pow(10, closestX) : closestX;
                HoverHeader.Text = $"Energy: {displayX:G5} MeV";

                HoverItems.ItemsSource = hoverData;
                HoverTooltip.Visibility = Microsoft.UI.Xaml.Visibility.Visible;

                // Get coordinates relative to the absolute RootGrid
                var relativePos = e.GetCurrentPoint(RootGrid).Position;

                HoverTooltip.UpdateLayout();
                double tooltipWidth = HoverTooltip.ActualWidth;
                double tooltipHeight = HoverTooltip.ActualHeight;

                double newX = relativePos.X + 20;
                double newY = relativePos.Y + 20;

                if (newX + tooltipWidth > RootGrid.ActualWidth) newX = relativePos.X - tooltipWidth - 20;
                if (newY + tooltipHeight > RootGrid.ActualHeight) newY = relativePos.Y - tooltipHeight - 20;

                Canvas.SetLeft(HoverTooltip, newX);
                Canvas.SetTop(HoverTooltip, newY);

                ScottPlot.Plottables.VerticalLine? vLine;
                if (!_plotCrosshairs.TryGetValue(plotControl, out vLine) || !plotControl.Plot.GetPlottables().Contains(vLine))
                {
                    vLine = plotControl.Plot.Add.VerticalLine(0, 1, ScottPlot.Colors.Gray);
                    vLine.LinePattern = ScottPlot.LinePattern.Dashed;
                    _plotCrosshairs[plotControl] = vLine;
                }

                vLine.X = closestX;
                vLine.IsVisible = true;
                plotControl.Refresh();
            }
        }

        private WinUIPlot GetActivePlot()
        {
            return ViewModel.SelectedMainTabIndex switch
            {
                0 => CrossSectionPlot,
                1 => MacPlot,
                2 => LacPlot,
                3 => DepthPlot,
                4 => ZeffPlot,
                5 => ViewModel.IsBuildupPlotActive ? BuildupPlot : ZeqPlot,
                _ => CrossSectionPlot
            };
        }

        private void UpdatePlots()
        {
            UpdateSpecificPlot(GetActivePlot());
        }

        private void UpdateSpecificPlot(WinUIPlot plot)
        {
            if (plot == null) return;

            if (plot == CrossSectionPlot) ViewModel.PlotTo(plot.Plot);
            else if (plot == MacPlot) ViewModel.PlotMacTo(plot.Plot);
            else if (plot == LacPlot) ViewModel.PlotLacTo(plot.Plot);
            else if (plot == DepthPlot) ViewModel.PlotDepthTo(plot.Plot);
            else if (plot == ZeffPlot) ViewModel.PlotZeffTo(plot.Plot);
            else if (plot == ZeqPlot) ViewModel.PlotZeqTo(plot.Plot);
            else if (plot == BuildupPlot) ViewModel.PlotBuildupTo(plot.Plot);

            plot.Refresh();
        }

        private async void ShowAlertDialog(string message, string title)
        {
            this.DispatcherQueue.TryEnqueue(async () =>
            {
                var dialog = new ContentDialog
                {
                    Title = title,
                    Content = message,
                    CloseButtonText = "OK",
                    XamlRoot = this.Content.XamlRoot
                };

                await dialog.ShowAsync();
            });
        }

        private async Task<bool> ShowConfirmationDialog(string title, string message, string primaryButtonText)
        {
            var tcs = new TaskCompletionSource<bool>();
            this.DispatcherQueue.TryEnqueue(async () =>
            {
                var dialog = new ContentDialog
                {
                    Title = title,
                    Content = message,
                    PrimaryButtonText = primaryButtonText,
                    CloseButtonText = "Cancel",
                    DefaultButton = ContentDialogButton.Close,
                    XamlRoot = this.Content.XamlRoot
                };

                var result = await dialog.ShowAsync();
                tcs.SetResult(result == ContentDialogResult.Primary);
            });
            return await tcs.Task;
        }

        private void MenuExit_Click(object sender, RoutedEventArgs e) => Application.Current.Exit();

        private void ThemeToggle_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel.IsDarkMode)
            {
                ThemeLight_Click(sender, e);
            }
            else
            {
                ThemeDark_Click(sender, e);
            }
        }

        private void ThemeLight_Click(object sender, RoutedEventArgs e)
        {
            RootGrid.RequestedTheme = ElementTheme.Light;
            ViewModel.IsDarkMode = false;
            UpdateTitleBarTheme(ElementTheme.Light);
            UpdateAllPlots();
            SaveThemePreference("Light");
        }

        private void ThemeDark_Click(object sender, RoutedEventArgs e)
        {
            RootGrid.RequestedTheme = ElementTheme.Dark;
            ViewModel.IsDarkMode = true;
            UpdateTitleBarTheme(ElementTheme.Dark);
            UpdateAllPlots();
            SaveThemePreference("Dark");
        }

        // --- THEME SAVING METHODS ---

        private readonly string _themeFilePath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Panex", "theme.txt");

        private void LoadThemePreference()
        {
            try
            {
                if (System.IO.File.Exists(_themeFilePath))
                {
                    string theme = System.IO.File.ReadAllText(_themeFilePath).Trim();
                    if (theme == "Dark")
                    {
                        RootGrid.RequestedTheme = ElementTheme.Dark;
                        if (ViewModel != null) ViewModel.IsDarkMode = true;
                        UpdateTitleBarTheme(ElementTheme.Dark);
                    }
                    else
                    {
                        RootGrid.RequestedTheme = ElementTheme.Light;
                        if (ViewModel != null) ViewModel.IsDarkMode = false;
                        UpdateTitleBarTheme(ElementTheme.Light);
                    }
                }
                else
                {
                    bool isDark = Application.Current.RequestedTheme == ApplicationTheme.Dark;
                    if (ViewModel != null) ViewModel.IsDarkMode = isDark;
                    UpdateTitleBarTheme(isDark ? ElementTheme.Dark : ElementTheme.Light);
                }
            }
            catch { /* Silently fail and use OS default if file can't be read */ }
        }

        private void SaveThemePreference(string theme)
        {
            try
            {
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_themeFilePath)!);
                System.IO.File.WriteAllText(_themeFilePath, theme);
            }
            catch { /* Silently fail if file can't be written */ }
        }

        private void UpdateAllPlots()
        {
            UpdateSpecificPlot(CrossSectionPlot);
            UpdateSpecificPlot(MacPlot);
            UpdateSpecificPlot(LacPlot);
            UpdateSpecificPlot(DepthPlot);
            UpdateSpecificPlot(ZeffPlot);
            UpdateSpecificPlot(ZeqPlot);
            UpdateSpecificPlot(BuildupPlot);
        }

        private void MaterialListView_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Delete)
            {
                if (ViewModel.CanDeleteMaterial)
                {
                    ViewModel.DeleteMaterialCommand.Execute(null);
                    e.Handled = true;
                }
            }
        }

        private async void SaveSvg_Click(object sender, RoutedEventArgs e)
        {
            var activePlot = GetActivePlot();
            if (activePlot == null) return;

            var picker = new Windows.Storage.Pickers.FileSavePicker();
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.PicturesLibrary;
            picker.FileTypeChoices.Add("Scalable Vector Graphics", new List<string>() { ".svg" });
            picker.FileTypeChoices.Add("PNG Image", new List<string>() { ".png" });

            string tabSuffix = ViewModel.SelectedMainTabIndex switch
            {
                0 => "CrossSection",
                1 => "MassAttenuation",
                2 => "LinearAttenuation",
                3 => "Depths",
                4 => "EffectiveZ",
                5 => ViewModel.IsBuildupPlotActive ? "Buildup" : "EquivalentZ",
                _ => "Plot"
            };
            string materialName = ViewModel.IsCompareMode
                ? "Comparison"
                : (ViewModel.SelectedMaterial?.Name ?? "Panex");
            picker.SuggestedFileName = $"{materialName}_{tabSuffix}";

            var file = await picker.PickSaveFileAsync();
            if (file != null)
            {
                if (file.FileType == ".svg")
                {
                    activePlot.Plot.SaveSvg(file.Path, 800, 600);
                }
                else
                {
                    activePlot.Plot.SavePng(file.Path, 800, 600);
                }
            }
        }

        private void DrawerDragHandle_DragDelta(object sender, Microsoft.UI.Xaml.Controls.Primitives.DragDeltaEventArgs e)
        {
            if (!_drawerOpen) return;

            double currentWidth = CreateDrawer.Width > 0 ? CreateDrawer.Width : CreateDrawer.ActualWidth;
            double newWidth = currentWidth - e.HorizontalChange;

            double minWidth = 520;
            double maxWidth = Math.Max(minWidth, Math.Min(1300, RootGrid.ActualWidth - 120));

            newWidth = Math.Max(minWidth, Math.Min(maxWidth, newWidth));
            _drawerWidth = newWidth;
            CreateDrawer.Width = newWidth;
        }
    }
}
