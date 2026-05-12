namespace OpenClawPTT.Services.Commands;

/// <summary>
/// Identifies the origin of a command — whether it is implemented natively by the PTT client
/// or forwarded to the OpenClaw gateway.
/// </summary>
public enum CommandSource
{
    /// <summary>Commands implemented and executed locally by the PTT client.</summary>
    Native,

    /// <summary>Commands forwarded to the OpenClaw gateway for remote execution.</summary>
    OpenClaw
}
