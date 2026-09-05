using System.Windows;
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
        public ImagePreviewWindow(ImageSource image)
        {
            InitializeComponent();
            PreviewImage.Source = image;
        }
    }
}
