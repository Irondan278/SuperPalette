using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using TaskImporter.Core.Clipboard;

namespace TaskImporterExtension.Services;

/// <summary>What the clipboard had to offer at capture time.</summary>
internal readonly record struct ClipboardCapture(string Text, string? AnchorText)
{
    public static ClipboardCapture Empty => new(string.Empty, null);

    public bool HasText => !string.IsNullOrWhiteSpace(Text);
}

/// <summary>
/// Reads the clipboard's text and HTML flavours through Win32 directly.
///
/// This lives here rather than in Core because Core is deliberately free of any
/// Windows dependency. It is also the whole reason the link-title feature is not
/// a Super Productivity plugin: a sandboxed renderer never sees CF_HTML, which
/// is where a copied link keeps its anchor text.
/// </summary>
internal static partial class WindowsClipboard
{
    private const uint CF_UNICODETEXT = 13;

    /// <summary>
    /// The clipboard is single-threaded-apartment only, and the extension's COM
    /// server runs MTA, so every read happens on a short-lived STA thread.
    /// </summary>
    public static ClipboardCapture Read()
    {
        var result = ClipboardCapture.Empty;

        var thread = new Thread(() =>
        {
            try
            {
                result = ReadCore();
            }
            catch (Exception)
            {
                // A clipboard held open by another process is ordinary, not an
                // error worth surfacing — the quick-add line just opens empty.
                result = ClipboardCapture.Empty;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();

        // The clipboard owner gets a fixed budget; a hung one must not hang the
        // palette along with it.
        if (!thread.Join(TimeSpan.FromSeconds(2))) return ClipboardCapture.Empty;

        return result;
    }

    private static ClipboardCapture ReadCore()
    {
        // Another process can hold the clipboard open, so a single failed
        // attempt means nothing. Retry briefly before giving up.
        for (var attempt = 0; attempt < 10; attempt++)
        {
            if (OpenClipboard(IntPtr.Zero)) break;
            if (attempt == 9) return ClipboardCapture.Empty;
            Thread.Sleep(20);
        }

        try
        {
            var text = ReadUnicodeText();
            var anchor = ReadHtmlLink();

            // A link copied from a browser puts the URL in the text flavour and
            // the words in the HTML one. With no text at all, the href is the
            // only thing worth pre-filling.
            if (string.IsNullOrWhiteSpace(text) && !string.IsNullOrWhiteSpace(anchor.Href))
            {
                text = anchor.Href!;
            }

            return new ClipboardCapture(
                text?.Trim() ?? string.Empty,
                anchor.HasText ? anchor.Text : null);
        }
        finally
        {
            CloseClipboard();
        }
    }

    private static string? ReadUnicodeText()
    {
        var handle = GetClipboardData(CF_UNICODETEXT);
        if (handle == IntPtr.Zero) return null;

        var pointer = GlobalLock(handle);
        if (pointer == IntPtr.Zero) return null;

        try
        {
            return Marshal.PtrToStringUni(pointer);
        }
        finally
        {
            GlobalUnlock(handle);
        }
    }

    /// <summary>
    /// Reads CF_HTML and hands the raw payload to Core's parser, which knows
    /// about the format's byte-offset header and its anchor extraction.
    /// </summary>
    private static ClipboardLink ReadHtmlLink()
    {
        var format = RegisterClipboardFormat("HTML Format");
        if (format == 0) return default;

        var handle = GetClipboardData(format);
        if (handle == IntPtr.Zero) return default;

        var pointer = GlobalLock(handle);
        if (pointer == IntPtr.Zero) return default;

        try
        {
            var size = (int)GlobalSize(handle);
            if (size <= 0) return default;

            var bytes = new byte[size];
            Marshal.Copy(pointer, bytes, 0, size);

            // CF_HTML is UTF-8 and NUL-terminated.
            var length = Array.IndexOf(bytes, (byte)0);
            if (length < 0) length = size;

            return HtmlClipboardParser.Parse(Encoding.UTF8.GetString(bytes, 0, length));
        }
        finally
        {
            GlobalUnlock(handle);
        }
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool OpenClipboard(IntPtr hWndNewOwner);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseClipboard();

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial IntPtr GetClipboardData(uint uFormat);

    [LibraryImport("user32.dll", EntryPoint = "RegisterClipboardFormatW", SetLastError = true,
        StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint RegisterClipboardFormat(string lpszFormat);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial IntPtr GlobalLock(IntPtr hMem);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GlobalUnlock(IntPtr hMem);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial UIntPtr GlobalSize(IntPtr hMem);
}
