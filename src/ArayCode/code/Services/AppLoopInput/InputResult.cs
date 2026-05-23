namespace ArayCode.Services;

/// <summary>
/// Return values from IInputHandler.HandleInputAsync.
/// </summary>
public enum InputResult
{
    Continue = 0,
    Quit = -1,
    Restart = 100,
}
