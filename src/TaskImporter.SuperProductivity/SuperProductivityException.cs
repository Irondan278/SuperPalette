using System;

namespace TaskImporter.SuperProductivity;

/// <summary>
/// Why a call to Super Productivity failed, in terms the palette can show to a
/// human without dumping a stack trace into a toast.
/// </summary>
public enum SuperProductivityFault
{
    /// <summary>Nothing is listening. Super Productivity is closed, or the API is off.</summary>
    NotRunning,

    /// <summary>The token is missing, wrong, or was regenerated.</summary>
    Unauthorized,

    /// <summary>The request was rejected — bad field, unknown project, and so on.</summary>
    Rejected,

    /// <summary>The app is up but the renderer has not finished loading.</summary>
    NotReady,

    /// <summary>Anything else.</summary>
    Unknown,
}

public sealed class SuperProductivityException : Exception
{
    public SuperProductivityException(
        SuperProductivityFault fault,
        string message,
        string? code = null,
        Exception? inner = null)
        : base(message, inner)
    {
        Fault = fault;
        Code = code;
    }

    public SuperProductivityFault Fault { get; }

    public string? Code { get; }

    /// <summary>A sentence worth putting in a toast.</summary>
    public string FriendlyMessage => Fault switch
    {
        SuperProductivityFault.NotRunning =>
            "Super Productivity isn't reachable. Is the desktop app running, and is the local API enabled under Settings → Misc?",
        SuperProductivityFault.Unauthorized =>
            "Super Productivity rejected the access token. Copy it again from Settings → Misc.",
        SuperProductivityFault.NotReady =>
            "Super Productivity is starting up. Try again in a moment.",
        _ => Message,
    };
}
