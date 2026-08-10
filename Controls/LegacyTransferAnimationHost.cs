using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace SearchBook.Controls;

/// <summary>
/// Recreates the compact, icon-based motion of classic Windows copy dialogs while
/// using the current operating system's real stock folder and document icons.
/// Windows 11 keeps the old shell AVI resource IDs as one-frame blank placeholders,
/// so loading stock icons at runtime is both visible and version-correct.
/// </summary>
public sealed class LegacyTransferAnimationHost : FrameworkElement
{
    private const uint ShgsiIcon = 0x000000100;
    private const uint ShgsiLargeIcon = 0x000000000;
    private const uint ShgfiIcon = 0x000000100;
    private const uint ShgfiLargeIcon = 0x000000000;
    private const uint ShgfiUseFileAttributes = 0x000000010;
    private const uint FileAttributeNormal = 0x00000080;
    private const uint SiidDocNoAssoc = 0;
    private const uint SiidFolder = 3;
    private const uint SiidFolderOpen = 4;

    private readonly DispatcherTimer _timer;
    private readonly ImageSource? _documentIcon;
    private readonly ImageSource? _sourceFolderIcon;
    private readonly ImageSource? _targetFolderIcon;
    private double _phase;
    private bool _isPlaying;

    public LegacyTransferAnimationHost()
    {
        SnapsToDevicePixels = true;
        UseLayoutRounding = true;
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.NearestNeighbor);

        _documentIcon = LoadFileTypeIcon(".txt") ?? LoadStockIcon(SiidDocNoAssoc);
        _sourceFolderIcon = LoadStockIcon(SiidFolderOpen) ?? LoadStockIcon(SiidFolder);
        _targetFolderIcon = LoadStockIcon(SiidFolder);

        _timer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(42)
        };
        _timer.Tick += (_, _) =>
        {
            _phase = (_phase + 0.035) % 1.0;
            InvalidateVisual();
        };
    }

    public void Start()
    {
        _isPlaying = true;
        _timer.Start();
        InvalidateVisual();
    }

    public void Stop()
    {
        _isPlaying = false;
        _timer.Stop();
        _phase = 0;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);

        var width = Math.Max(120, ActualWidth);
        var height = Math.Max(28, ActualHeight);
        var folderSize = Math.Min(28, height - 4);
        var documentSize = Math.Min(20, height - 8);
        var folderY = Math.Round((height - folderSize) / 2);
        var leftX = 8d;
        var rightX = width - folderSize - 8;

        DrawIconOrFallback(drawingContext, _sourceFolderIcon,
            new Rect(leftX, folderY, folderSize, folderSize), Colors.Goldenrod, true);
        DrawIconOrFallback(drawingContext, _targetFolderIcon,
            new Rect(rightX, folderY, folderSize, folderSize), Colors.Goldenrod, true);

        var progress = _isPlaying ? SmoothStep(_phase) : 0.05;
        var startX = leftX + folderSize - 3;
        var endX = rightX - documentSize + 4;
        var paperX = startX + ((endX - startX) * progress);
        var arc = Math.Sin(progress * Math.PI) * Math.Min(15, height * 0.22);
        var paperY = Math.Round((height - documentSize) / 2 - arc);

        if (_isPlaying)
        {
            var trailPen = new Pen(new SolidColorBrush(Color.FromArgb(80, 87, 113, 126)), 1);
            trailPen.Freeze();
            drawingContext.DrawLine(trailPen,
                new Point(Math.Max(startX, paperX - 17), paperY + documentSize - 3),
                new Point(paperX - 4, paperY + documentSize - 3));
        }

        DrawIconOrFallback(drawingContext, _documentIcon,
            new Rect(Math.Round(paperX), paperY, documentSize, documentSize), Colors.White, false);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsInfinity(availableSize.Width) ? 190 : availableSize.Width;
        var height = double.IsInfinity(availableSize.Height) ? 30 : availableSize.Height;
        return new Size(Math.Max(120, width), Math.Max(28, height));
    }

    private static double SmoothStep(double value) => value * value * (3 - (2 * value));

    private static void DrawIconOrFallback(
        DrawingContext context, ImageSource? icon, Rect bounds, Color fallbackColor, bool folder)
    {
        if (icon is not null)
        {
            context.DrawImage(icon, bounds);
            return;
        }

        var fill = new SolidColorBrush(fallbackColor);
        var outline = new Pen(new SolidColorBrush(Color.FromRgb(91, 104, 112)), 1);
        fill.Freeze();
        outline.Freeze();
        context.DrawRoundedRectangle(fill, outline, bounds, 1, 1);
        if (!folder)
        {
            var line = new Pen(Brushes.SlateGray, 1);
            line.Freeze();
            context.DrawLine(line,
                new Point(bounds.Left + 5, bounds.Top + 8),
                new Point(bounds.Right - 5, bounds.Top + 8));
        }
    }

    private static ImageSource? LoadStockIcon(uint stockIconId)
    {
        var info = new StockIconInfo
        {
            Size = (uint)Marshal.SizeOf<StockIconInfo>()
        };

        if (SHGetStockIconInfo(stockIconId, ShgsiIcon | ShgsiLargeIcon, ref info) != 0 ||
            info.Icon == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            var source = Imaging.CreateBitmapSourceFromHIcon(
                info.Icon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }
        finally
        {
            DestroyIcon(info.Icon);
        }
    }

    private static ImageSource? LoadFileTypeIcon(string extension)
    {
        var info = new ShellFileInfo();
        var result = SHGetFileInfo(extension, FileAttributeNormal, ref info,
            (uint)Marshal.SizeOf<ShellFileInfo>(),
            ShgfiIcon | ShgfiLargeIcon | ShgfiUseFileAttributes);
        if (result == IntPtr.Zero || info.Icon == IntPtr.Zero) return null;

        try
        {
            var source = Imaging.CreateBitmapSourceFromHIcon(
                info.Icon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }
        finally
        {
            DestroyIcon(info.Icon);
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct StockIconInfo
    {
        public uint Size;
        public IntPtr Icon;
        public int SystemImageIndex;
        public int IconIndex;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string Path;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShellFileInfo
    {
        public IntPtr Icon;
        public int IconIndex;
        public uint Attributes;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string DisplayName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string TypeName;
    }

    [DllImport("shell32.dll")]
    private static extern int SHGetStockIconInfo(uint stockIconId, uint flags, ref StockIconInfo info);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(
        string path, uint fileAttributes, ref ShellFileInfo info, uint infoSize, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr icon);
}
