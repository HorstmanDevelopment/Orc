namespace Orc.Core.Orchitect;

internal static class MissionPreamble
{
    public static string BuildAnalysisPrompt(string? mission, string body) =>
        string.IsNullOrWhiteSpace(mission)
            ? body
            : $$"""
                Mission for this repository (use this to focus your analysis — only propose enhancements that advance this mission; ignore unrelated improvements): {{mission!.Trim()}}
                ---

                {{body}}
                """;

    public static string BuildPlanningPrompt(string? mission, string body) =>
        string.IsNullOrWhiteSpace(mission)
            ? body
            : $$"""
                Mission for this repository (the enhancement below was identified to advance this mission; keep your step aligned with it): {{mission!.Trim()}}

                ---

                {{body}}
                """;
}
