
using OpenClawPTT.Services;

namespace OpenClawPTT.ConfigWizard;

public static class PromptHelper
{
    public static void PrintPrePromptMessage(IStreamShellHost host)
    {
        host.AddMessage("");
        host.AddMessage("↓↓↓↓↓↓↓↓↓↓↓↓↓↓↓↓↓↓↓↓↓↓↓↓↓↓↓↓↓↓↓↓↓↓↓↓");
    }
}