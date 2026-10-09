namespace Wording.Core;

public static class PracticeTopics
{
    // This is a vocabulary classification, rather than a scenario to practice.
    public static bool IsScenarioTopic(string category) =>
        !string.Equals(category.Trim(), "動詞片語", StringComparison.OrdinalIgnoreCase);
}
