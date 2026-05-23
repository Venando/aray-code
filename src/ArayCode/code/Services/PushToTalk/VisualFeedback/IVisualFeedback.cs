using System;

namespace ArayCode.VisualFeedback;

public interface IVisualFeedback : IDisposable
{
    void Show();
    void Hide();
}