using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using HdrImageViewer.Pages;
using HdrImageViewer.Presentation;
using HdrImageViewer.Services;
using HdrImageViewer.ViewModels;
using System.ComponentModel;
using System.Numerics;
using System.Runtime.InteropServices;
using Windows.Graphics;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace HdrImageViewer;

public sealed partial class MainWindow : Window
{
    private const int InitialWindowWidthDips = 1320;
    private const int InitialWindowHeightDips = 840;
    private const int MinimumWindowWidthDips = 420;
    private const int MinimumWindowHeightDips = 320;
    private const int WindowPositionDebounceMilliseconds = 100;
    private const int SwRestore = 9;

    // Windows Photos-style title-bar toolbar auto-hide. The row and its native
    // caption buttons always stay in place; only the content is hidden, so the
    // window keeps its supported drag/resize regions. 56 DIPs is the 48 DIP
    // title-bar row plus a small forgiveness margin into the image area.
    private const double ViewerToolbarRevealZoneDips = 56.0;
    private const int ViewerToolbarWatchIntervalMilliseconds = 120;
    private const int ViewerToolbarHideDelayFloorMilliseconds = 250;

    private readonly IReadOnlyList<string> _activationFilePaths;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _windowPositionTimer;
    private IReadOnlyList<string>? _pendingExternalFilePaths;
    private bool _isInitialNavigationComplete;
    private bool _isImmersiveViewing;
    private bool _isClosed;
    private CommandBar? _viewerCommandBar;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _viewerToolbarWatchTimer;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _viewerToolbarHideTimer;
    private bool _isViewerToolbarContentVisible = true;
    private bool _isViewerToolbarAutoHideActive;
    private readonly PointerEventHandler _viewerToolbarPointerMovedHandler;

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(IntPtr hWnd);

    [StructLayout(LayoutKind.Sequential)]
    private struct Win32Point
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Win32Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out Win32Point point);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ScreenToClient(IntPtr hWnd, ref Win32Point point);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetClientRect(IntPtr hWnd, out Win32Rect rect);

    public event EventHandler<bool>? ImmersiveViewingChanged;

    public bool IsImmersiveViewing => _isImmersiveViewing;

    public string? CurrentOpenFilePath =>
        ViewerPage.ViewModel.HasImage && !string.IsNullOrWhiteSpace(ViewerPage.ViewModel.FilePath)
            ? ViewerPage.ViewModel.FilePath
            : null;

    public MainWindow(IReadOnlyList<string>? activationFilePaths = null)
    {
        _activationFilePaths = activationFilePaths ?? [];
        InitializeComponent();
        _viewerCommandBar = ViewerPage.TakeCommandBar();
        _viewerCommandBar.Opening += ViewerCommandBar_Opening;
        _viewerCommandBar.Closing += ViewerCommandBar_Closing;
        ViewerCommandsHost.Children.Add(_viewerCommandBar);
        _viewerToolbarPointerMovedHandler = WindowRoot_PointerMoved;
        WindowRoot.AddHandler(
            UIElement.PointerMovedEvent,
            _viewerToolbarPointerMovedHandler,
            handledEventsToo: true);
        WindowRoot.AddHandler(
            UIElement.KeyDownEvent,
            new KeyEventHandler(WindowRoot_KeyDownHandledToo),
            handledEventsToo: true);
        var visibilityMotion = new VisibilityMotion(DispatcherQueue);
        visibilityMotion.Add(NavFrame, new Vector3(0, 24, 0));
        visibilityMotion.Add(ViewerPage, Vector3.Zero);
        ViewerPage.ConfigureVisibilityMotion(visibilityMotion);
        Closed += (_, _) => visibilityMotion.Dispose();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
        AppWindow.SetIcon("Assets/AppIcon.ico");
        ViewerPage.ViewModel.PropertyChanged += ViewerViewModel_PropertyChanged;
        UpdateWindowTitle();
        ResizeToInitialDesignSize();
        AppWindow.Changed += AppWindow_Changed;
        AppSettingsService.SettingsChanged += AppSettingsService_SettingsChanged;
        Closed += MainWindow_Closed;
        WindowRoot.ActualThemeChanged += WindowRoot_ActualThemeChanged;
        ApplyTheme(AppSettingsService.Current.Theme);
        RefreshViewerToolbarChrome();
    }

    private void ResizeToInitialDesignSize()
    {
        var hwnd = Win32Interop.GetWindowFromWindowId(AppWindow.Id);
        var scale = GetDpiForWindow(hwnd) / 96.0;
        AppWindow.Resize(new SizeInt32(
            (int)Math.Round(InitialWindowWidthDips * scale),
            (int)Math.Round(InitialWindowHeightDips * scale)));
    }

    private void MainWindow_Closed(object sender, WindowEventArgs args)
    {
        if (_isClosed)
        {
            return;
        }

        _isClosed = true;
        _windowPositionTimer?.Stop();
        _viewerToolbarWatchTimer?.Stop();
        _viewerToolbarHideTimer?.Stop();
        if (_viewerCommandBar is not null)
        {
            _viewerCommandBar.Opening -= ViewerCommandBar_Opening;
            _viewerCommandBar.Closing -= ViewerCommandBar_Closing;
            _viewerCommandBar = null;
        }

        WindowRoot.RemoveHandler(UIElement.PointerMovedEvent, _viewerToolbarPointerMovedHandler);
        WindowRoot.ActualThemeChanged -= WindowRoot_ActualThemeChanged;
        ViewerPage.ViewModel.PropertyChanged -= ViewerViewModel_PropertyChanged;
        AppWindow.Changed -= AppWindow_Changed;
        AppSettingsService.SettingsChanged -= AppSettingsService_SettingsChanged;
        App.UnregisterSingleInstance();
        ViewerPage.Shutdown();
    }

    private void AppSettingsService_SettingsChanged(object? sender, EventArgs e)
    {
        ApplyTheme(AppSettingsService.Current.Theme);
        RefreshViewerToolbarChrome();
    }

    private void ApplyTheme(AppTheme theme)
    {
        WindowRoot.RequestedTheme = theme switch
        {
            AppTheme.Light => ElementTheme.Light,
            AppTheme.Dark => ElementTheme.Dark,
            _ => ElementTheme.Default,
        };
        UpdateCaptionTheme();
    }

    private void WindowRoot_ActualThemeChanged(FrameworkElement sender, object args) => UpdateCaptionTheme();

    private void UpdateCaptionTheme() => AppWindow.TitleBar.PreferredTheme =
        WindowRoot.ActualTheme == ElementTheme.Dark ? TitleBarTheme.Dark : TitleBarTheme.Light;

    private void ViewerTitleContent_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        // Keep a readable filename and a native drag region as commands overflow.
        ViewerCommandsHost.MaxWidth = Math.Max(48, e.NewSize.Width - Math.Min(360, e.NewSize.Width * 0.35) - 24);
        AppTitleBar.RecomputeDragRegions();
    }

    private void ViewerCommandsHost_SizeChanged(object sender, SizeChangedEventArgs e) => AppTitleBar.RecomputeDragRegions();

    private void WindowRoot_KeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        if (_isViewerToolbarAutoHideActive && !_isViewerToolbarContentVisible)
        {
            // Keyboard users cannot focus collapsed toolbar content, so any key
            // press reveals the title-bar commands again (Windows Photos shows
            // its chrome on interaction as well).
            _viewerToolbarHideTimer?.Stop();
            SetViewerToolbarContentVisible(true);
        }

        if (ViewerPage.Visibility == Visibility.Visible)
            ViewerPage.HandleViewerKeyDown(sender, e);
    }

    /// <summary>
    /// Fallback for keys that a child control (typically the filmstrip ListView)
    /// already marked handled: image navigation still has to happen. The flag set
    /// by the page's own handler prevents navigating twice.
    /// </summary>
    private async void WindowRoot_KeyDownHandledToo(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        if (_isClosed || ViewerPage.Visibility != Visibility.Visible)
        {
            return;
        }

        if (ViewerPage.ConsumeViewerArrowKeyHandledByPage())
        {
            return;
        }

        if (e.Handled)
        {
            await ViewerPage.HandleViewerArrowKeyAfterChildAsync(e);
        }
    }

    private void ViewerViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ImageWorkspaceViewModel.FileName) or nameof(ImageWorkspaceViewModel.HasImage))
        {
            UpdateWindowTitle();
        }

        if (e.PropertyName is nameof(ImageWorkspaceViewModel.HasImage))
        {
            // Opening/closing an image changes whether the title-bar commands
            // auto-hide; the toolbar is hidden by default while an image is shown.
            RefreshViewerToolbarChrome();
        }
    }

    private void UpdateWindowTitle()
    {
        var fileName = ViewerPage.ViewModel.FileName;
        var title = ViewerPage.ViewModel.HasImage && !string.IsNullOrWhiteSpace(fileName)
            ? fileName
            : Localization.GetString("AppTitle");
        if (NavFrame.Visibility == Visibility.Visible)
            title = Localization.GetString("AppTitle");
        var showingContentPage = NavFrame.Visibility == Visibility.Visible;
        ApplyViewerToolbarContentVisibility();
        AppTitleBar.Title = showingContentPage ? title : string.Empty;
        ViewerFileName.Text = title;
        ToolTipService.SetToolTip(ViewerFileName, title);
        AutomationProperties.SetName(AppTitleBar, title);
        AppWindow.Title = title;
    }

    private async void WindowRoot_Loaded(object sender, RoutedEventArgs e)
    {
        if (_isInitialNavigationComplete)
        {
            return;
        }

        _isInitialNavigationComplete = true;
        try
        {
            UpdateCaptionTheme();
            ShowViewerPage();
            var initialPaths = _pendingExternalFilePaths ?? _activationFilePaths;
            _pendingExternalFilePaths = null;
            if (initialPaths.Count > 0)
            {
                await ViewerPage.OpenImagePathsAsync(initialPaths);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            App.LogUnhandledUiException("MainWindow.WindowRoot_Loaded", ex);
        }
    }

    public async Task ActivateAndOpenFilesAsync(IReadOnlyList<string> filePaths)
    {
        if (_isClosed)
        {
            return;
        }

        try
        {
            var hwnd = Win32Interop.GetWindowFromWindowId(AppWindow.Id);
            if (hwnd != IntPtr.Zero)
            {
                if (IsIconic(hwnd))
                {
                    _ = ShowWindow(hwnd, SwRestore);
                }

                _ = SetForegroundWindow(hwnd);
            }
        }
        catch
        {
        }

        Activate();
        ShowViewerPage();

        if (filePaths.Count == 0)
        {
            return;
        }

        if (!_isInitialNavigationComplete)
        {
            _pendingExternalFilePaths = filePaths;
            return;
        }

        await ViewerPage.OpenImagePathsAsync(filePaths);
    }

    public void NavigateToSettings() => ShowContentPage(typeof(SettingsPage));

    public void NavigateToAbout() => ShowContentPage(typeof(AboutPage));

    private void TitleBar_BackRequested(TitleBar sender, object args)
    {
        ShowViewerPage();
    }

    private void ShowViewerPage()
    {
        ViewerPage.Visibility = Visibility.Visible;
        NavFrame.Visibility = Visibility.Collapsed;
        NavFrame.BackStack.Clear();
        AppTitleBar.IsBackButtonVisible = false;
        UpdateWindowTitle();
        RefreshViewerToolbarChrome();
    }

    private void ShowContentPage(Type pageType)
    {
        AppTitleBar.IsBackButtonVisible = true;
        AppTitleBar.IsBackButtonEnabled = true;
        if (NavFrame.CurrentSourcePageType != pageType)
        {
            // The persistent frame gets the same entrance on first and repeat
            // visits. Suppress Frame's extra transition to avoid double motion.
            NavFrame.Navigate(pageType, null, new SuppressNavigationTransitionInfo());
            NavFrame.BackStack.Clear();
        }
        ViewerPage.Visibility = Visibility.Collapsed;
        NavFrame.Visibility = Visibility.Visible;
        UpdateWindowTitle();
        RefreshViewerToolbarChrome();
    }

    public bool ToggleImmersiveViewing()
    {
        SetImmersiveViewing(!IsImmersiveViewing);
        return IsImmersiveViewing;
    }

    public void SetImmersiveViewing(bool isImmersive)
    {
        if (isImmersive)
        {
            AppWindow.SetPresenter(AppWindowPresenterKind.FullScreen);
        }
        else
        {
            AppWindow.SetPresenter(AppWindowPresenterKind.Default);
        }

        ApplyImmersiveShell(isImmersive);
    }

    private void AppWindow_Changed(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (args.DidSizeChange)
        {
            EnforceMinimumWindowSize(sender);
        }

        if (args.DidPresenterChange)
        {
            ApplyImmersiveShell(sender.Presenter.Kind == AppWindowPresenterKind.FullScreen);
        }

        if (args.DidPositionChange && !_isClosed)
        {
            ScheduleWindowPositionNotification();
        }
    }

    private void ScheduleWindowPositionNotification()
    {
        if (_windowPositionTimer is null)
        {
            _windowPositionTimer = DispatcherQueue.CreateTimer();
            _windowPositionTimer.Interval = TimeSpan.FromMilliseconds(WindowPositionDebounceMilliseconds);
            _windowPositionTimer.IsRepeating = false;
            _windowPositionTimer.Tick += (_, _) =>
            {
                if (!_isClosed)
                {
                    ViewerPage.NotifyWindowPositionChanged();
                }
            };
        }

        _windowPositionTimer.Stop();
        _windowPositionTimer.Start();
    }

    private static void EnforceMinimumWindowSize(AppWindow window)
    {
        if (window.Presenter.Kind == AppWindowPresenterKind.FullScreen)
        {
            return;
        }

        var hwnd = Win32Interop.GetWindowFromWindowId(window.Id);
        var scale = GetDpiForWindow(hwnd) / 96.0;
        var minimumWidth = (int)Math.Ceiling(MinimumWindowWidthDips * scale);
        var minimumHeight = (int)Math.Ceiling(MinimumWindowHeightDips * scale);
        if (window.Size.Width < minimumWidth || window.Size.Height < minimumHeight)
        {
            window.Resize(new SizeInt32(
                Math.Max(window.Size.Width, minimumWidth),
                Math.Max(window.Size.Height, minimumHeight)));
        }
    }

    // ---- Viewer toolbar auto-hide -----------------------------------------
    //
    // In the normal viewer (image shown, not the settings/about frame, not
    // immersive) the title-bar CommandBar and the file-name row are hidden by
    // default and revealed Windows Photos style when the pointer approaches the
    // top edge. WinUI's TitleBar cannot be fully collapsed without also losing
    // the native caption buttons and the supported drag/resize regions, so the
    // title-bar row height and caption buttons are left untouched and only the
    // content row (ViewerTitleContent) is collapsed. The empty row therefore
    // stays a valid drag region for the window.

    private void ViewerCommandBar_Opening(object? sender, object e)
    {
        if (!_isViewerToolbarAutoHideActive)
        {
            return;
        }

        _viewerToolbarHideTimer?.Stop();
        SetViewerToolbarContentVisible(true);
    }

    private void ViewerCommandBar_Closing(object? sender, object e)
    {
        if (!_isViewerToolbarAutoHideActive)
        {
            return;
        }

        ScheduleViewerToolbarHide();
    }

    private void WindowRoot_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_isViewerToolbarAutoHideActive || WindowRoot is null)
        {
            return;
        }

        var position = e.GetCurrentPoint(WindowRoot).Position;
        if (position.Y > ViewerToolbarRevealZoneDips)
        {
            return;
        }

        _viewerToolbarHideTimer?.Stop();
        if (!_isViewerToolbarContentVisible)
        {
            SetViewerToolbarContentVisible(true);
        }
    }

    /// <summary>
    /// Recomputes whether the title-bar content should auto-hide and applies the
    /// initial state. Called when the viewer mode, image, immersive state or the
    /// auto-hide delay setting changes.
    /// </summary>
    private void RefreshViewerToolbarChrome()
    {
        if (_isClosed)
        {
            return;
        }

        var viewerMode = NavFrame.Visibility != Visibility.Visible
            && ViewerPage.Visibility == Visibility.Visible;
        var delayMilliseconds = AppUserSettings.NormalizeViewerChromeAutoHideMilliseconds(
            AppSettingsService.Current.ViewerChromeAutoHideMilliseconds);
        // The floating-toolbar setting is authoritative: its documented 0 value
        // ("never hide") must also pin the title-bar toolbar, so the user can
        // keep the commands permanently visible. The shipped default (2200 ms)
        // still gives the Windows Photos-style hide-by-default behaviour.
        var canAutoHide = viewerMode
            && !_isImmersiveViewing
            && ViewerPage.ViewModel.HasImage
            && delayMilliseconds > 0;
        var wasActive = _isViewerToolbarAutoHideActive;
        _isViewerToolbarAutoHideActive = canAutoHide;
        if (!canAutoHide)
        {
            _viewerToolbarWatchTimer?.Stop();
            _viewerToolbarHideTimer?.Stop();
            SetViewerToolbarContentVisible(true);
            return;
        }

        EnsureViewerToolbarTimers();
        var hideDelay = Math.Max(delayMilliseconds, ViewerToolbarHideDelayFloorMilliseconds);
        if (_viewerToolbarHideTimer is { } hideTimer
            && Math.Abs(hideTimer.Interval.TotalMilliseconds - hideDelay) > 1.0)
        {
            hideTimer.Interval = TimeSpan.FromMilliseconds(hideDelay);
        }

        _viewerToolbarWatchTimer!.Start();
        if (!wasActive)
        {
            // Entering the auto-hide mode (image opened, back from settings or
            // immersive): hidden by default unless the user is already at the top.
            _viewerToolbarHideTimer?.Stop();
            SetViewerToolbarContentVisible(ViewerToolbarShouldStayVisible());
        }
    }

    private void EnsureViewerToolbarTimers()
    {
        if (_viewerToolbarWatchTimer is null)
        {
            var watchTimer = DispatcherQueue.CreateTimer();
            watchTimer.Interval = TimeSpan.FromMilliseconds(ViewerToolbarWatchIntervalMilliseconds);
            watchTimer.IsRepeating = true;
            watchTimer.Tick += ViewerToolbarWatchTimer_Tick;
            _viewerToolbarWatchTimer = watchTimer;
        }

        if (_viewerToolbarHideTimer is null)
        {
            var hideTimer = DispatcherQueue.CreateTimer();
            hideTimer.IsRepeating = false;
            hideTimer.Tick += ViewerToolbarHideTimer_Tick;
            _viewerToolbarHideTimer = hideTimer;
        }
    }

    private void ViewerToolbarWatchTimer_Tick(Microsoft.UI.Dispatching.DispatcherQueueTimer sender, object args)
    {
        if (!_isViewerToolbarAutoHideActive)
        {
            sender.Stop();
            return;
        }

        if (!_isViewerToolbarContentVisible)
        {
            _viewerToolbarHideTimer?.Stop();
            if (ViewerToolbarShouldStayVisible())
            {
                SetViewerToolbarContentVisible(true);
            }

            return;
        }

        if (ViewerToolbarShouldStayVisible())
        {
            _viewerToolbarHideTimer?.Stop();
            return;
        }

        ScheduleViewerToolbarHide();
    }

    private void ViewerToolbarHideTimer_Tick(Microsoft.UI.Dispatching.DispatcherQueueTimer sender, object args)
    {
        sender.Stop();
        if (!_isViewerToolbarAutoHideActive
            || !_isViewerToolbarContentVisible
            || ViewerToolbarShouldStayVisible())
        {
            return;
        }

        SetViewerToolbarContentVisible(false);
    }

    private void ScheduleViewerToolbarHide()
    {
        if (_viewerToolbarHideTimer is { IsRunning: false })
        {
            _viewerToolbarHideTimer.Start();
        }
    }

    private bool ViewerToolbarShouldStayVisible()
    {
        if (_viewerCommandBar?.IsOpen == true)
        {
            return true;
        }

        if (ViewerToolbarHasKeyboardFocus())
        {
            return true;
        }

        return IsPointerInViewerToolbarRevealZone();
    }

    private bool ViewerToolbarHasKeyboardFocus()
    {
        var xamlRoot = ViewerTitleContent.XamlRoot;
        if (xamlRoot is null)
        {
            return false;
        }

        if (FocusManager.GetFocusedElement(xamlRoot) is not Control
            { FocusState: FocusState.Keyboard or FocusState.Programmatic } focused)
        {
            return false;
        }

        for (DependencyObject? current = focused; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (ReferenceEquals(current, ViewerTitleContent))
            {
                return true;
            }
        }

        return false;
    }

    private bool IsPointerInViewerToolbarRevealZone()
    {
        if (!GetCursorPos(out var point))
        {
            return false;
        }

        var hwnd = Win32Interop.GetWindowFromWindowId(AppWindow.Id);
        if (hwnd == IntPtr.Zero
            || !GetClientRect(hwnd, out var clientRect)
            || clientRect.Right <= clientRect.Left
            || clientRect.Bottom <= clientRect.Top
            || !ScreenToClient(hwnd, ref point))
        {
            return false;
        }

        if (point.X < clientRect.Left || point.X > clientRect.Right
            || point.Y < clientRect.Top || point.Y > clientRect.Bottom)
        {
            return false;
        }

        var scale = GetDpiForWindow(hwnd) / 96.0;
        if (scale <= 0.0)
        {
            scale = 1.0;
        }

        var yDips = (point.Y - clientRect.Top) / scale;
        return yDips >= 0.0 && yDips <= ViewerToolbarRevealZoneDips;
    }

    private void SetViewerToolbarContentVisible(bool visible)
    {
        if (_isViewerToolbarContentVisible == visible)
        {
            ApplyViewerToolbarContentVisibility();
            return;
        }

        _isViewerToolbarContentVisible = visible;
        ApplyViewerToolbarContentVisibility();
    }

    private void ApplyViewerToolbarContentVisibility()
    {
        var showingContentPage = NavFrame.Visibility == Visibility.Visible;
        var target = !showingContentPage && _isViewerToolbarContentVisible
            ? Visibility.Visible
            : Visibility.Collapsed;
        if (ViewerTitleContent.Visibility == target)
        {
            return;
        }

        ViewerTitleContent.Visibility = target;
        // Recompute after layout so the emptied title-bar row becomes the drag
        // region again (or the command bar becomes interactive when revealed).
        DispatcherQueue.TryEnqueue(() =>
        {
            if (_isClosed)
            {
                return;
            }

            try
            {
                AppTitleBar.RecomputeDragRegions();
            }
            catch (Exception ex)
            {
                App.LogUnhandledUiException("MainWindow.RecomputeDragRegions", ex);
            }
        });
    }

    private void ApplyImmersiveShell(bool isImmersive)
    {
        if (_isImmersiveViewing == isImmersive)
        {
            return;
        }

        _isImmersiveViewing = isImmersive;
        AppTitleBar.Visibility = isImmersive ? Visibility.Collapsed : Visibility.Visible;
        TitleBarRow.Height = isImmersive ? new GridLength(0) : new GridLength(48);
        RefreshViewerToolbarChrome();
        ImmersiveViewingChanged?.Invoke(this, isImmersive);
    }
}
