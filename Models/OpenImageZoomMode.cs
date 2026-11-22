namespace HdrImageViewer.Models;

/// <summary>
/// Zoom level the viewer applies when a new image is opened.
/// </summary>
public enum OpenImageZoomMode
{
    Fit = 0,
    ActualSize = 1,
    Last = 2,
}
