using OpenClawPTT.Services;

namespace OpenClawPTT.ConfigWizard;

public static class ConfigSelectionHelper
{
    public static string GetTitle(this IConfigSectionWizard section) => $"[bold cyan]▶ {section.Name}:[/] ";


    public static void PrintSubSection(IStreamShellHost host, string sectionName, string? description = null)
    {
        host.AddMessage("");
        if (description != null)
            host.AddMessage($"──────▶ [bold cyan2]{sectionName.ToUpperInvariant()}[/] {description}:");
        else
            host.AddMessage($"──────▶ [bold cyan2]{sectionName.ToUpperInvariant()}[/]");
    }

    //public static string GetTitle(this IConfigSectionWizard section) => $"[bold cyan]▶ {section.Name}[/] [grey]- {section.Description}[/]";
}