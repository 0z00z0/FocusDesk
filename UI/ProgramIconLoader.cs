using System.Collections.Concurrent;
using System.Runtime.InteropServices.WindowsRuntime;
using FocusDesk.Helpers;
using FocusDesk.Services;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace FocusDesk.UI;

/// <summary>
/// Draws program icons off the thread that draws the window: the shell reads each one on a background
/// single-threaded-apartment thread, and the pixels become an image back on the window's thread.
/// </summary>
/// <remarks>One thread for the process, started on first use and left running: a queue that is empty
/// costs a blocked thread and nothing else. Images are kept per location for the life of the process,
/// so a list opened twice draws from memory the second time.</remarks>
internal static class ProgramIconLoader
{
    /// <summary>Drawn at the size the Start menu uses for a list row.</summary>
    public const int Size = 32;

    private static readonly BlockingCollection<(string Location, DispatcherQueue Queue, Action<ImageSource> Done)> _queue = [];
    private static readonly ConcurrentDictionary<string, ImageSource> _drawn = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Lazy<Thread> _thread = new(Start);

    /// <summary>Asks for the icon at <paramref name="location"/>; <paramref name="done"/> runs on the
    /// calling thread once it is drawn, and never where the shell draws nothing.</summary>
    public static void Request(string? location, Action<ImageSource> done)
    {
        if (string.IsNullOrWhiteSpace(location)) return;
        if (_drawn.TryGetValue(location, out var image)) { done(image); return; }

        var queue = DispatcherQueue.GetForCurrentThread();
        if (queue is null) return;

        _ = _thread.Value;
        _queue.Add((location, queue, done));
    }

    private static Thread Start()
    {
        var thread = new Thread(Run) { IsBackground = true, Name = "FocusDesk program icons" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return thread;
    }

    private static void Run()
    {
        foreach (var (location, queue, done) in _queue.GetConsumingEnumerable())
        {
            try
            {
                if (ProgramIcons.Load(location, Size) is not { } pixels) continue;
                queue.TryEnqueue(() =>
                {
                    try
                    {
                        var image = _drawn.GetOrAdd(location, _ => Image(pixels));
                        done(image);
                    }
                    catch (Exception ex) { AppLog.Error("ProgramIconLoader.Show", ex); }
                });
            }
            catch (Exception ex) { AppLog.Error("ProgramIconLoader.Run", ex); }
        }
    }

    private static WriteableBitmap Image(IconPixels pixels)
    {
        var bitmap = new WriteableBitmap(pixels.Width, pixels.Height);
        using (var stream = bitmap.PixelBuffer.AsStream())
            stream.Write(pixels.Bgra, 0, pixels.Bgra.Length);
        bitmap.Invalidate();
        return bitmap;
    }
}
