namespace FocusDesk.Helpers;

/// <summary>Safe, share-tolerant file-append primitive behind <see cref="FocusDesk.Services.CsvSampleStore"/>.
/// See docs/build-notes.md ("SafeFileAppend.cs") for the design reasoning.</summary>
internal static class SafeFileAppend
{
    private const int MaxAttempts = 5;

    /// <summary>Appends <paramref name="content"/>, retrying transient sharing violations. Throws the
    /// final exception if every attempt fails.</summary>
    internal static void Append(string path, string content) => Write(path, content, throwOnFail: true);

    /// <summary>Like <see cref="Append"/> but never throws.</summary>
    internal static bool TryAppend(string path, string content) => Write(path, content, throwOnFail: false);

    private static bool Write(string path, string content, bool throwOnFail)
    {
        Exception? last = null;
        for (var attempt = 0; attempt < MaxAttempts; attempt++)
        {
            try
            {
                using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
                using var writer = new StreamWriter(stream);
                writer.Write(content);
                return true;
            }
            catch (DirectoryNotFoundException ex) { last = ex; break; } // non-transient — dir is gone
            catch (FileNotFoundException ex)      { last = ex; break; } // non-transient
            catch (IOException ex)
            {
                // Transient: a sibling's append is mid-flight, or AV briefly holds the handle.
                last = ex;
                if (attempt < MaxAttempts - 1) Thread.Sleep(15 * (attempt + 1));
            }
            catch (Exception ex) { last = ex; break; } // UnauthorizedAccess / PathTooLong — retry won't help
        }

        if (throwOnFail && last is not null) throw last;
        return false;
    }
}
