using ArayCode;

namespace ArayCode.VisualFeedback;

internal sealed class NoVisualFeedback : IVisualFeedback
{
    public NoVisualFeedback(AppConfig? config = null) { }
    public void Show() { }
    public void Hide() { }
    public void Dispose() { }
}