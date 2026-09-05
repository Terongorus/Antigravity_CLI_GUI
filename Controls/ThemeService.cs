using Microsoft.VisualStudio.PlatformUI;
using System;
using System.ComponentModel;

namespace TeronClaudeCodeVS.Controls
{
    /// <summary>
    /// Live "is the current Visual Studio color theme dark?" signal, driving which half of each
    /// _light/_dark icon pair in Resources/IconGeometries.xaml is shown (see that file's own header
    /// comment for what the suffixes actually mean - it's the icon's own color, not the VS theme it
    /// targets, so a dark VS theme shows the "_light" variant and vice versa).
    /// <para>
    /// Deliberately NOT wired up via a static XAML markup extension ({x:Static}/{StaticResource}):
    /// that would evaluate <see cref="Instance"/> - and so call the real VS SDK color-theme API - the
    /// moment ClaudeCodeChatControl.xaml is parsed, which happens in every xUnit test that
    /// constructs a ChatControl (see ChatControl.cs's own note on why ClaudeCodePackage.Instance is
    /// never touched during construction there either). Outside a real devenv.exe process
    /// VSColorTheme has no shell to read from and its behavior there is not something this
    /// static-instance class should gamble the whole test suite on. Consumers instead call
    /// <see cref="Refresh"/> and read <see cref="IsDarkTheme"/> from a Loaded handler, matching how
    /// this codebase already defers every other real-VS-service read until after construction.
    /// </para>
    /// </summary>
    internal sealed class ThemeService : INotifyPropertyChanged
    {
        public static ThemeService Instance { get; } = new();

        public event PropertyChangedEventHandler? PropertyChanged;

        private bool _isDarkTheme = true;
        private bool _subscribed;

        /// <summary>True when the icon's own light-colored variant should be shown (i.e. the
        /// current VS theme is dark). Defaults to true so a caller that forgets to call
        /// <see cref="Refresh"/> at least gets a real answer's worth of coin-flip, not a guaranteed
        /// wrong one - most of this extension's own testing has been against VS's dark theme.</summary>
        public bool IsDarkTheme
        {
            get => _isDarkTheme;
            private set
            {
                if (_isDarkTheme == value) return;
                _isDarkTheme = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsDarkTheme)));
            }
        }

        private ThemeService()
        {
        }

        /// <summary>
        /// Re-reads the current VS theme and starts listening for live theme changes, if it hasn't
        /// already. Safe to call from every icon's Loaded handler - only the first call actually
        /// does anything beyond the initial read.
        /// </summary>
        public void Refresh()
        {
            IsDarkTheme = ComputeIsDarkTheme();

            if (_subscribed) return;
            try
            {
                VSColorTheme.ThemeChanged += OnVsThemeChanged;
                _subscribed = true;
            }
            catch
            {
                // No real VS shell to subscribe to (e.g. a design-time or test host) - IsDarkTheme
                // just stays at whatever ComputeIsDarkTheme() returned above.
            }
        }

        private void OnVsThemeChanged(ThemeChangedEventArgs e) => IsDarkTheme = ComputeIsDarkTheme();

        private static bool ComputeIsDarkTheme()
        {
            try
            {
                System.Drawing.Color bg = VSColorTheme.GetThemedColor(EnvironmentColors.ToolWindowBackgroundColorKey);
                double luminance = (0.299 * bg.R + 0.587 * bg.G + 0.114 * bg.B) / 255.0;
                return luminance < 0.5;
            }
            catch
            {
                return true;
            }
        }
    }
}
