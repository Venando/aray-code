using System.Threading;
using System.Threading.Tasks;

namespace ArayCode.Services;

/// <summary>Abstraction for console input handling.</summary>
public interface IInputHandler
{
    Task<InputResult> HandleInputAsync(CancellationToken ct);

    Task SendTextAsync(string text, CancellationToken ct);
}
