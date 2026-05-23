
using ArayCode.Services;
using ArayCode.Services.Themes;

namespace ArayCode.ConfigWizard;

public static class PromptHelper
{
    public static void PrintPrePromptMessage(IStreamShellHost host)
    {
        host.AddMessage("");
        host.AddMessage("↓↓↓↓↓↓↓↓↓↓↓↓↓↓↓↓↓↓↓↓↓↓↓↓↓↓↓↓↓↓↓↓↓↓↓↓");
    }
}