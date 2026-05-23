using ArayCode.Services;
using ArayCode.Services.Themes;

namespace ArayCode.ConfigWizard;

public static class ConfigSelectionHelper
{
    public static string GetTitle(this IConfigSectionWizard section) => $"[{ThemeProvider.Current.Tools.Panel.SectionHeader}]▶ {section.Name}:[/] ";


    public static void PrintSubSection(IStreamShellHost host, string sectionName, string? description = null)
    {
        host.AddMessage("");
        if (description != null)
            host.AddMessage($"──────▶ [{ThemeProvider.Current.Tools.Panel.SectionHeader}]{sectionName.ToUpperInvariant()}[/] {description}:");
        else
            host.AddMessage($"──────▶ [{ThemeProvider.Current.Tools.Panel.SectionHeader}]{sectionName.ToUpperInvariant()}[/]");
    }

    //public static string GetTitle(this IConfigSectionWizard section) => $"[bold cyan]▶ {section.Name}[/] [grey]- {section.Description}[/]";
}