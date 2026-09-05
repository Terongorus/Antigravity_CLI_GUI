using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace TeronClaudeCodeVS.Controls
{
    /// <summary>A lightweight, non-modal full-size viewer for a sent image attachment - matches the
    /// official VS Code extension's own click-to-preview behavior. Deliberately not a VS document
    /// tab: a pasted screenshot never had a file on disk to open one for in the first place, so an
    /// in-app viewer over the already-decoded bitmap is the only thing that works for every image
    /// attachment, pasted or dropped alike.</summary>
    public partial class ImagePreviewWindow : Window
    {
        private const double MinZoom = 1.0;
        private const double MaxZoom = 8.0;

        public ImagePreviewWindow(ImageSource image)
        {
            InitializeComponent();
            PreviewImage.Source = image;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            // Freeze the window at its initial fit-to-screen size so zooming in grows the image
            // inside a fixed viewport (showing scrollbars) instead of SizeToContent continuing to
            // resize the window itself on every subsequent layout change.
            Width = ActualWidth;
            Height = ActualHeight;
            SizeToContent = SizeToContent.Manual;
        }

        private void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            switch (Keyboard.Modifiers)
            {
                case ModifierKeys.Control:
                    double factor = e.Delta > 0 ? 1.1 : 1 / 1.1;
                    double newZoom = Math.Min(MaxZoom, Math.Max(MinZoom, ImageScale.ScaleX * factor));
                    ImageScale.ScaleX = newZoom;
                    ImageScale.ScaleY = newZoom;
                    e.Handled = true;
                    break;

                // ScrollViewer has no built-in Shift+Wheel-scrolls-horizontally behavior of its own -
                // an unmodified wheel always drives the vertical offset regardless of Shift, so this
                // is needed explicitly to get the conventional "Shift+Wheel = horizontal" scrolling
                // once zoomed in wide enough for a horizontal scrollbar to appear.
                case ModifierKeys.Shift:
                    PreviewScroll.ScrollToHorizontalOffset(PreviewScroll.HorizontalOffset - e.Delta);
                    e.Handled = true;
                    break;

                // No modifier: leave unhandled, ScrollViewer's own default vertical-scroll behavior
                // already handles this - no need to duplicate it here.
            }
        }
    }
}
