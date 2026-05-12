using System.Threading;

namespace OpenClawPTT.ConfigWizard;

/// <summary>
/// Centralized ref-counted state tracking whether any wizard (config, agent config,
/// first-connection) is currently active. Prevents message sending and agent
/// switching while any wizard is running.
///
/// Uses <see cref="Interlocked"/> so nested or overlapping wizards work correctly:
/// each Enter() must be paired with a Leave(), and IsActive stays true until
/// the last active wizard exits.
/// </summary>
public static class WizardState
{
    private static int _activeCount;

    /// <summary>True when at least one wizard is active.</summary>
    public static bool IsActive => Volatile.Read(ref _activeCount) > 0;

    /// <summary>Marks wizard entry. Must be paired with <see cref="Leave"/>.</summary>
    public static void Enter() => Interlocked.Increment(ref _activeCount);

    /// <summary>Marks wizard exit. Must be paired with a prior <see cref="Enter"/>.</summary>
    public static void Leave() => Interlocked.Decrement(ref _activeCount);
}
