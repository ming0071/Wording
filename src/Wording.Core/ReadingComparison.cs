using System.Text.RegularExpressions;

namespace Wording.Core;

public enum ReadingDifferenceKind { Match, Different, Missing, Extra }
public sealed record ReadingDifference(ReadingDifferenceKind Kind, string Expected, string Heard);
public sealed record ReadingComparison(ReadingDifference[] Words)
{
    public bool IsMatch => Words.Length > 0 && Words.All(x => x.Kind == ReadingDifferenceKind.Match);
    public int Matched => Words.Count(x => x.Kind == ReadingDifferenceKind.Match);
    public int Different => Words.Count(x => x.Kind == ReadingDifferenceKind.Different);
    public int Missing => Words.Count(x => x.Kind == ReadingDifferenceKind.Missing);
    public int Extra => Words.Count(x => x.Kind == ReadingDifferenceKind.Extra);

    private static readonly IReadOnlyDictionary<string, string> Expansions = new Dictionary<string, string>
    {
        ["i'm"] = "i am", ["you're"] = "you are", ["we're"] = "we are", ["they're"] = "they are",
        ["i've"] = "i have", ["you've"] = "you have", ["we've"] = "we have", ["they've"] = "they have",
        ["i'll"] = "i will", ["you'll"] = "you will", ["we'll"] = "we will", ["they'll"] = "they will",
        ["don't"] = "do not", ["doesn't"] = "does not", ["didn't"] = "did not", ["isn't"] = "is not",
        ["aren't"] = "are not", ["wasn't"] = "was not", ["weren't"] = "were not", ["can't"] = "can not",
        ["cannot"] = "can not", ["couldn't"] = "could not", ["won't"] = "will not", ["wouldn't"] = "would not",
        ["shouldn't"] = "should not", ["haven't"] = "have not", ["hasn't"] = "has not", ["hadn't"] = "had not"
    };
    private static readonly string[] Numbers = ["zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine",
        "ten", "eleven", "twelve", "thirteen", "fourteen", "fifteen", "sixteen", "seventeen", "eighteen", "nineteen", "twenty"];

    public static ReadingComparison Compare(string expected, string heard)
    {
        var a = Tokens(expected); var b = Tokens(heard);
        if (a.Length > 500 || b.Length > 500) throw new ArgumentException("請一次練習一個較短的例句。");
        var distance = new int[a.Length + 1, b.Length + 1];
        for (var i = 0; i <= a.Length; i++) distance[i, 0] = i;
        for (var j = 0; j <= b.Length; j++) distance[0, j] = j;
        for (var i = 1; i <= a.Length; i++)
            for (var j = 1; j <= b.Length; j++)
                distance[i, j] = Math.Min(distance[i - 1, j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1),
                    Math.Min(distance[i - 1, j] + 1, distance[i, j - 1] + 1));
        var differences = new List<ReadingDifference>();
        var row = a.Length; var column = b.Length;
        while (row > 0 || column > 0)
        {
            if (row > 0 && column > 0 && distance[row, column] == distance[row - 1, column - 1] + (a[row - 1] == b[column - 1] ? 0 : 1))
            {
                differences.Add(new(a[row - 1] == b[column - 1] ? ReadingDifferenceKind.Match : ReadingDifferenceKind.Different, a[--row], b[--column]));
            }
            else if (row > 0 && distance[row, column] == distance[row - 1, column] + 1)
                differences.Add(new(ReadingDifferenceKind.Missing, a[--row], ""));
            else differences.Add(new(ReadingDifferenceKind.Extra, "", b[--column]));
        }
        differences.Reverse();
        return new(differences.ToArray());
    }

    private static string[] Tokens(string text) => Regex.Matches(text.Replace('’', '\'').ToLowerInvariant(), @"[a-z]+(?:'[a-z]+)?|\d+")
        .SelectMany(match => Expansions.TryGetValue(match.Value, out var expanded) ? expanded.Split(' ') :
            new[] { int.TryParse(match.Value, out var number) && number is >= 0 and <= 20 ? Numbers[number] : match.Value }).ToArray();
}
