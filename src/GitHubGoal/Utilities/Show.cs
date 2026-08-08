using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace GitHubGoal.Utilities;

/// <summary>
/// Helpers for x:Bind function bindings.
///
/// Used instead of IValueConverter because the XAML root here is a Window: the
/// generated binding code calls SetConverterLookupRoot with the root object, which
/// only accepts a FrameworkElement, so any {StaticResource} converter on a Window
/// fails to compile. Static function bindings sidestep that entirely.
/// </summary>
public static class Show
{
    public static Visibility When(bool condition) =>
        condition ? Visibility.Visible : Visibility.Collapsed;

    public static Visibility Unless(bool condition) =>
        condition ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>Visible only when the string has content, so optional lines collapse.</summary>
    public static Visibility WhenPresent(string? value) =>
        string.IsNullOrWhiteSpace(value) ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>
    /// The avatar image, or null for a missing or malformed URL so the placeholder
    /// glyph shows through instead of the binding failing.
    /// </summary>
    public static ImageSource? Avatar(string? url) =>
        !string.IsNullOrWhiteSpace(url) && Uri.TryCreate(url, UriKind.Absolute, out var uri)
            ? new BitmapImage(uri)
            : null;
}
