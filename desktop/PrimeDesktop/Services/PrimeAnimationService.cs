using System;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace PrimeDesktop.Services;

/// <summary>
/// Central animation service providing signature Prime open/close animations,
/// easing curves, and transitions across all desktop windows and screens.
/// </summary>
public static class PrimeAnimationService
{
    // Signature Animation Curves matching Android Prime UI
    public static readonly IEasingFunction EntranceStandardEase = new CubicEase { EasingMode = EasingMode.EaseOut };
    public static readonly IEasingFunction EntranceSpringEase = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.32 };
    public static readonly IEasingFunction ExitStandardEase = new CubicEase { EasingMode = EasingMode.EaseIn };
    public static readonly IEasingFunction ExitCardEase = new CubicEase { EasingMode = EasingMode.EaseIn };

    public static readonly TimeSpan DurationStandardEntrance = TimeSpan.FromMilliseconds(240);
    public static readonly TimeSpan DurationDialogEntrance = TimeSpan.FromMilliseconds(300);
    public static readonly TimeSpan DurationStandardExit = TimeSpan.FromMilliseconds(170);
    public static readonly TimeSpan DurationDialogExit = TimeSpan.FromMilliseconds(160);

    /// <summary>
    /// Hooks signature Prime opening and closing animations onto a Window and its root content element.
    /// Handles entrance on Loaded/Show, and intercepts Closing to play exit animation before actual destruction.
    /// </summary>
    public static void HookBrandTransitions(Window window, FrameworkElement? rootElement = null, bool isDialog = false)
    {
        if (window == null) return;

        rootElement ??= window.Content as FrameworkElement;
        if (rootElement == null) return;

        rootElement.RenderTransformOrigin = new Point(0.5, 0.5);

        ScaleTransform scaleTransform;
        TranslateTransform translateTransform;

        if (rootElement.RenderTransform is TransformGroup tg)
        {
            scaleTransform = tg.Children.OfType<ScaleTransform>().FirstOrDefault() ?? new ScaleTransform(1, 1);
            if (!tg.Children.Contains(scaleTransform)) tg.Children.Add(scaleTransform);

            translateTransform = tg.Children.OfType<TranslateTransform>().FirstOrDefault() ?? new TranslateTransform(0, 0);
            if (!tg.Children.Contains(translateTransform)) tg.Children.Add(translateTransform);
        }
        else
        {
            var group = new TransformGroup();
            scaleTransform = new ScaleTransform(1, 1);
            translateTransform = new TranslateTransform(0, 0);
            group.Children.Add(scaleTransform);
            group.Children.Add(translateTransform);
            rootElement.RenderTransform = group;
        }

        // On window loaded, play entrance animation
        window.Loaded += (s, e) =>
        {
            ThemeManager.ApplyWindowDwm(window);
            WindowUtils.EnableWindowMinMaxHook(window);
            PlayWindowEntrance(rootElement, scaleTransform, translateTransform, isDialog);
        };

        // Intercept closing to play exit animation
        bool isExiting = false;
        bool? pendingDialogResult = null;

        window.Closing += (s, e) =>
        {
            if (isExiting) return;

            pendingDialogResult = window.DialogResult;
            e.Cancel = true;
            isExiting = true;

            PlayWindowExit(rootElement, scaleTransform, translateTransform, () =>
            {
                try
                {
                    if (pendingDialogResult.HasValue)
                    {
                        window.DialogResult = pendingDialogResult.Value;
                    }
                    else
                    {
                        window.Close();
                    }
                }
                catch
                {
                    window.Close();
                }
            }, isDialog);
        };
    }

    public static void PlayWindowEntrance(
        FrameworkElement rootElement, 
        ScaleTransform? scaleTransform = null, 
        TranslateTransform? translateTransform = null, 
        bool isDialog = false,
        Action? onCompleted = null)
    {
        if (rootElement == null) return;

        var duration = isDialog ? DurationDialogEntrance : DurationStandardEntrance;
        var scaleEase = isDialog ? EntranceSpringEase : EntranceStandardEase;
        var fadeEase = EntranceStandardEase;

        double fromScale = isDialog ? 0.84 : 0.95;
        double fromY = isDialog ? 35.0 : 16.0;

        // 1. Fade In
        rootElement.Opacity = 0.0;
        var fadeAnim = new DoubleAnimation(0.0, 1.0, duration) { EasingFunction = fadeEase };
        if (onCompleted != null)
        {
            fadeAnim.Completed += (s, e) => onCompleted();
        }
        rootElement.BeginAnimation(UIElement.OpacityProperty, fadeAnim);

        // 2. Scale In
        if (scaleTransform != null)
        {
            var scaleXAnim = new DoubleAnimation(fromScale, 1.0, duration) { EasingFunction = scaleEase };
            var scaleYAnim = new DoubleAnimation(fromScale, 1.0, duration) { EasingFunction = scaleEase };
            scaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, scaleXAnim);
            scaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, scaleYAnim);
        }

        // 3. Translate In
        if (translateTransform != null)
        {
            var transYAnim = new DoubleAnimation(fromY, 0.0, duration) { EasingFunction = EntranceStandardEase };
            translateTransform.BeginAnimation(TranslateTransform.YProperty, transYAnim);
        }
    }

    public static void PlayWindowExit(
        FrameworkElement rootElement, 
        ScaleTransform? scaleTransform = null, 
        TranslateTransform? translateTransform = null, 
        Action? onCompleted = null,
        bool isDialog = false)
    {
        if (rootElement == null)
        {
            onCompleted?.Invoke();
            return;
        }

        var duration = isDialog ? DurationDialogExit : DurationStandardExit;
        var ease = isDialog ? ExitCardEase : ExitStandardEase;

        double toScale = isDialog ? 0.88 : 0.96;
        double toY = isDialog ? 24.0 : 12.0;

        // 1. Fade Out
        var fadeAnim = new DoubleAnimation(1.0, 0.0, duration) { EasingFunction = ease };
        fadeAnim.Completed += (s, e) =>
        {
            onCompleted?.Invoke();
        };
        rootElement.BeginAnimation(UIElement.OpacityProperty, fadeAnim);

        // 2. Scale Out
        if (scaleTransform != null)
        {
            var scaleXAnim = new DoubleAnimation(1.0, toScale, duration) { EasingFunction = ease };
            var scaleYAnim = new DoubleAnimation(1.0, toScale, duration) { EasingFunction = ease };
            scaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, scaleXAnim);
            scaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, scaleYAnim);
        }

        // 3. Translate Out
        if (translateTransform != null)
        {
            var transYAnim = new DoubleAnimation(0.0, toY, duration) { EasingFunction = ease };
            translateTransform.BeginAnimation(TranslateTransform.YProperty, transYAnim);
        }
    }
}
