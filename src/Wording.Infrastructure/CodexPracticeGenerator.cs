using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Wording.Core;

namespace Wording.Infrastructure;

public sealed partial class CodexContentGenerator
{
    public async Task<PracticeMaterial> GeneratePracticeAsync(PracticeRequest request, CancellationToken token = default)
    {
        request.Options.Validate();
        if (request.Targets.Length is < 1 or > 20 || request.Targets.Select(x => x.Id).Distinct().Count() != request.Targets.Length)
            throw new ArgumentException("請選擇 1–20 個不同詞義。");
        if (!await gate.WaitAsync(0, token)) throw new InvalidOperationException("已有內容正在生成，請稍候或先取消。");
        string? schemaPath = null;
        try
        {
            var promptDirectory = Path.Combine(AppContext.BaseDirectory, "Prompts");
            var common = await File.ReadAllTextAsync(Path.Combine(promptDirectory, "practice-common.txt"), token);
            var modePrompt = await File.ReadAllTextAsync(Path.Combine(promptDirectory,
                request.Options.Mode == PracticeMode.Reading ? "practice-reading.txt" : "practice-listening.txt"), token);
            await CheckAvailabilityAsync(token);
            ReserveRequest();
            schemaPath = Path.Combine(workDirectory, $"schema-{Guid.NewGuid():N}.json");
            await File.WriteAllTextAsync(schemaPath, BuildPracticeSchema(request), token);
            var input = new { options = request.Options, mode = request.Options.Mode.ToString(),
                type = request.Options.Kind.ToString(), level = request.Options.Level.ToString(),
                wordRange = new { minimum = WordRange(request.Options.Length).Minimum, maximum = WordRange(request.Options.Length).Maximum },
                targets = request.Targets.Select(x => new { senseId = x.Id, headword = x.Headword, partOfSpeech = x.PartOfSpeech, intendedMeaning = x.Meaning }) };
            var response = await runner.RunAsync(settings.ExecutablePath, BuildArguments(schemaPath),
                common + "\n" + modePrompt + "\nINPUT DATA:\n" + JsonSerializer.Serialize(input, new JsonSerializerOptions(JsonSerializerDefaults.Web)), workDirectory,
                TimeSpan.FromSeconds(300), token);
            token.ThrowIfCancellationRequested();
            if (response.ExitCode != 0) throw new InvalidOperationException(DescribeFailure(response.StandardError));
            return ParsePracticeResponse(ExtractFinal(response.StandardOutput), request);
        }
        finally
        {
            try { if (schemaPath is not null && File.Exists(schemaPath)) File.Delete(schemaPath); }
            finally { gate.Release(); }
        }
    }

    public static (int Minimum, int Maximum) WordRange(PracticeLength length) => length switch
    {
        PracticeLength.Short => (120, 180), PracticeLength.Medium => (220, 300), _ => (350, 500)
    };

    public static PracticeMaterial ParsePracticeResponse(string json, PracticeRequest request)
    {
        request.Options.Validate();
        var result = JsonSerializer.Deserialize<PracticeMaterial>(json, VocabularyFileService.JsonOptions)
            ?? throw new InvalidDataException("練習內容為空。");
        bool Text(string? value, int maximum) => !string.IsNullOrWhiteSpace(value) && value.Length <= maximum;
        bool English(string? value, int maximum) => Text(value, maximum) && !Regex.IsMatch(value!, @"[\u3400-\u9fff]");
        bool Chinese(string? value, int maximum) => Text(value, maximum) && Regex.IsMatch(value!, @"[\u3400-\u9fff]");
        void Require(bool condition, string message) { if (!condition) throw new InvalidDataException(message + " 請重新生成。"); }
        Require(English(result.Title, 160) && English(result.Passage, 15000) && Chinese(result.Translation, 15000), "文章或翻譯格式不完整。");
        var words = Regex.Matches(result.Passage, @"\b[A-Za-z]+(?:['’-][A-Za-z]+)*\b").Count;
        var range = WordRange(request.Options.Length);
        Require(words >= range.Minimum && words <= range.Maximum, $"文章有 {words} 個英文單字，所選長度需要 {range.Minimum}–{range.Maximum} 個。");
        Require(result.Questions is not null && result.Questions.Length == request.Options.QuestionCount, $"需要 {request.Options.QuestionCount} 題，但收到 {result.Questions?.Length ?? 0} 題。");
        // Detail, purpose and inference are reading comprehension subtypes, not invalid reading questions.
        if (request.Options.Mode == PracticeMode.Reading)
            result = result with { Questions = result.Questions!.Select(q => q is not null && q.Kind is "detail" or "purpose" or "inference"
                ? q with { Kind = "comprehension" } : q!).ToArray() };
        var kinds = request.Options.Mode == PracticeMode.Reading ? new[] { "comprehension", "vocabulary", "grammar" } : ["comprehension", "inference", "purpose", "detail"];
        for (var index = 0; index < result.Questions!.Length; index++)
        {
            var question = result.Questions[index];
            var label = $"第 {index + 1} 題";
            Require(question is not null && kinds.Contains(question.Kind), $"{label}的題型不適用目前模式。");
            Require(English(question!.Prompt, 2000), $"{label}缺少有效的英文題目。");
            Require(question!.Options is { Length: 4 } && question.Options.All(x => English(x, 600)) &&
                question.Options.Select(x => x.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 4 &&
                question.AnswerIndex is >= 0 and < 4, $"{label}需要四個不同的英文選項與一個正解。");
            Require(Chinese(question.Explanation, 3000), $"{label}缺少中文解析。");
            Require(Text(question.Evidence, 2000) && result.Passage.Contains(question.Evidence, StringComparison.Ordinal), $"{label}的依據無法在文章中找到。");
            Require(question.OptionExplanations is { Length: 4 } && question.OptionExplanations.All(x => Chinese(x, 1500)), $"{label}的選項缺少中文解析。");
        }
        Require(result.Questions!.Select(x => x.Prompt.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() == result.Questions.Length, "題目重複。");
        if (request.Options.Mode == PracticeMode.Reading)
            Require(kinds.All(kind => result.Questions.Any(x => x.Kind == kind)), "閱讀需包含理解、單字與文法題。");
        Require(result.Targets is not null && result.Targets.Length == request.Targets.Length &&
            result.Targets.All(x => x is not null) && result.Targets.Select(x => x.SenseId).Distinct().Count() == result.Targets.Length,
            $"需要 {request.Targets.Length} 個不同目標詞義，但收到的目標詞義數量不符或有重複。");
        foreach (var target in request.Targets)
        {
            var usage = result.Targets!.SingleOrDefault(x => x.SenseId == target.Id);
            Require(usage is not null && Text(usage.Surface, 200) && Chinese(usage.Meaning, 1500) && Text(usage.Evidence, 2000) &&
                result.Passage.Contains(usage.Evidence, StringComparison.Ordinal) && usage.Evidence.Contains(usage.Surface, StringComparison.OrdinalIgnoreCase), $"目標詞「{target.Headword}」缺少文章中的使用依據。");
            // The prompt requests the supplied spelling so a model cannot silently swap a target for an unrelated word.
            Require(string.Equals(usage!.Surface.Trim(), target.Headword.Trim(), StringComparison.OrdinalIgnoreCase), $"目標詞「{target.Headword}」的拼字與指定詞不符。");
        }
        return result;
    }

    private static string BuildPracticeSchema(PracticeRequest request)
    {
        var schema = JsonNode.Parse(PracticeSchema)!;
        var questions = schema["properties"]!["questions"]!;
        questions["minItems"] = request.Options.QuestionCount;
        questions["maxItems"] = request.Options.QuestionCount;
        questions["items"]!["properties"]!["kind"]!["enum"] = JsonSerializer.SerializeToNode(
            request.Options.Mode == PracticeMode.Reading ? new[] { "comprehension", "vocabulary", "grammar" }
                : ["comprehension", "inference", "purpose", "detail"]);
        var targets = schema["properties"]!["targets"]!;
        targets["minItems"] = request.Targets.Length;
        targets["maxItems"] = request.Targets.Length;
        targets["items"]!["properties"]!["senseId"]!["enum"] = JsonSerializer.SerializeToNode(request.Targets.Select(x => x.Id.ToString("D")));
        return schema.ToJsonString();
    }

    private const string PracticeSchema = """
    {"type":"object","additionalProperties":false,"required":["title","passage","translation","questions","targets"],"properties":{
      "title":{"type":"string"},"passage":{"type":"string"},"translation":{"type":"string"},
      "questions":{"type":"array","minItems":3,"maxItems":5,"items":{"type":"object","additionalProperties":false,
        "required":["kind","prompt","options","answerIndex","explanation","evidence","optionExplanations"],"properties":{
        "kind":{"type":"string","enum":["comprehension","vocabulary","grammar","inference","purpose","detail"]},
        "prompt":{"type":"string"},"options":{"type":"array","minItems":4,"maxItems":4,"items":{"type":"string"}},
        "answerIndex":{"type":"integer","minimum":0,"maximum":3},"explanation":{"type":"string"},"evidence":{"type":"string"},
        "optionExplanations":{"type":"array","minItems":4,"maxItems":4,"items":{"type":"string"}}}}},
      "targets":{"type":"array","items":{"type":"object","additionalProperties":false,"required":["senseId","surface","meaning","evidence"],
        "properties":{"senseId":{"type":"string"},"surface":{"type":"string"},"meaning":{"type":"string"},"evidence":{"type":"string"}}}}
    }}
    """;
}
