using System;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace PrimeDesktop.Services;

/// <summary>
/// Central animation service providing native Windows Fluent Design window transitions,
/// deceleration easing curves, and smooth dialog animations across all desktop windows.
/// </summary>
public static class PrimeAnimationService
{
    // Native Windows 11 Fluent Design Easing Curves (Decelerate on entrance, accelerate on exit)
    public static readonly IEasingFunction EntranceStandardEase = new CubicEase { EasingMode = EasingMode.EaseOut };
    public static readonly IEasingFunction EntranceDialogEase = new CubicEase { EasingMode = EasingMode.EaseOut };
    public static readonly IEasingFunction ExitStandardEase = new CubicEase { EasingMode = EasingMode.EaseIn };
    public static readonly IEasingFunction ExitDialogEase = new CubicEase { EasingMode = EasingMode.EaseIn };

    public static readonly TimeSpan DurationStandardEntrance = TimeSpan.FromMilliseconds(200);
    public static readonly TimeSpan DurationDialogEntrance = TimeSpan.FromMilliseconds(220);
    public static readonly TimeSpan DurationStandardExit = TimeSpan.FromMilliseconds(130);
    public static readonly TimeSpan DurationDialogExit = TimeSpan.FromMilliseconds(130);

    /// <summary>
    /// Hooks native Windows Fluent opening and closing animations onto a Window and its root content element.
    /// Handles entrance on Loaded, and intercepts Closing to play exit animation before actual destruction.
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

        // On window loaded, apply DWM frame and play Windows Fluent entrance animation
        window.Loaded += (s, e) =>
        {
            ThemeManager.ApplyWindowDwm(window);
            WindowUtils.EnableNativeWindowAnimations(window);
            WindowUtils.EnableWindowMinMaxHook(window);
            PlayWindowEntrance(rootElement, scaleTransform, translateTransform, isDialog);
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
        var ease = isDialog ? EntranceDialogEase : EntranceStandardEase;

        // Windows Fluent standard: subtle scale 0.96 -> 1.0, subtle vertical float 12px -> 0
        double fromScale = isDialog ? 0.95 : 0.97;
        double fromY = isDialog ? 16.0 : 10.0;

        // 1. Fade In
        rootElement.Opacity = 0.0;
        var fadeAnim = new DoubleAnimation(0.0, 1.0, duration) { EasingFunction = ease };
        if (onCompleted != null)
        {
            fadeAnim.Completed += (s, e) => onCompleted();
        }
        rootElement.BeginAnimation(UIElement.OpacityProperty, fadeAnim);

        // 2. Scale In
        if (scaleTransform != null)
        {
            var scaleXAnim = new DoubleAnimation(fromScale, 1.0, duration) { EasingFunction = ease };
            var scaleYAnim = new DoubleAnimation(fromScale, 1.0, duration) { EasingFunction = ease };
            scaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, scaleXAnim);
            scaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, scaleYAnim);
        }

        // 3. Translate In
        if (translateTransform != null)
        {
            var transYAnim = new DoubleAnimation(fromY, 0.0, duration) { EasingFunction = ease };
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
        var ease = isDialog ? ExitDialogEase : ExitStandardEase;

        double toScale = isDialog ? 0.97 : 0.98;
        double toY = isDialog ? 10.0 : 8.0;

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
