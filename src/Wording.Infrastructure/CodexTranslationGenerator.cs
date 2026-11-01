using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Wording.Core;

namespace Wording.Infrastructure;

public sealed partial class CodexContentGenerator : ITranslationGenerator
{
    private static readonly JsonSerializerOptions TranslationJsonOptions = new(VocabularyFileService.JsonOptions)
    { Converters = { new JsonStringEnumConverter(allowIntegerValues: false) } };
    public async Task<TranslationMaterial> GenerateTranslationAsync(TranslationRequest request, CancellationToken token = default)
    {
        ValidateTranslationRequest(request);
        var json = await RunTranslationAsync("translation-generate.txt", TranslationSchema(GenerationSchema, request),
            new { targets = TranslationTargets(request) }, token);
        return ParseTranslationMaterial(json, request);
    }

    public async Task<TranslationAssessment> AssessTranslationAsync(TranslationSubmission submission, CancellationToken token = default)
    {
        ValidateTranslationRequest(submission.Request);
        ValidateTranslationMaterial(submission.Material, submission.Request);
        if (submission.Answers is null || submission.Answers.Length != submission.Request.Targets.Length ||
            submission.Answers.Any(x => x is null || !TranslationText(x.English, 2000)) ||
            !submission.Answers.Select(x => x.SenseId).Order().SequenceEqual(submission.Request.Targets.Select(x => x.Id).Order()))
            throw new ArgumentException("請完成每題的英文答案，每題最多 2000 字元。");
        var json = await RunTranslationAsync("translation-assess.txt", TranslationSchema(AssessmentSchema, submission.Request),
            new { targets = TranslationTargets(submission.Request), questions = submission.Material.Questions, answers = submission.Answers }, token);
        return ParseTranslationAssessment(json, submission.Request);
    }

    private async Task<string> RunTranslationAsync(string promptFile, string schema, object input, CancellationToken token)
    {
        if (!await gate.WaitAsync(0, token)) throw new InvalidOperationException("已有內容正在生成，請稍候或先取消。");
        string? schemaPath = null;
        try
        {
            var prompt = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Prompts", promptFile), token);
            await CheckAvailabilityAsync(token);
            ReserveRequest();
            schemaPath = Path.Combine(workDirectory, $"schema-{Guid.NewGuid():N}.json");
            await File.WriteAllTextAsync(schemaPath, schema, token);
            var response = await runner.RunAsync(settings.ExecutablePath, BuildArguments(schemaPath),
                prompt + "\nINPUT DATA:\n" + JsonSerializer.Serialize(input, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
                workDirectory, TimeSpan.FromSeconds(ApplicationConfiguration.Current.Ai.PracticeTimeoutSeconds), token);
            token.ThrowIfCancellationRequested();
            if (response.ExitCode != 0) throw new InvalidOperationException(DescribeFailure(response.StandardError));
            return ExtractFinal(response.StandardOutput);
        }
        finally
        {
            try { if (schemaPath is not null && File.Exists(schemaPath)) File.Delete(schemaPath); }
            finally { gate.Release(); }
        }
    }

    private static object TranslationTargets(TranslationRequest request) => request.Targets.Select(x =>
        new { senseId = x.Id, headword = x.Headword, partOfSpeech = x.PartOfSpeech, intendedMeaning = x.Meaning });

    private static void ValidateTranslationRequest(TranslationRequest request)
    {
        if (request.Targets is null || request.Targets.Length is < 1 or > 5 ||
            request.Targets.Any(x => x is null || x.Id == Guid.Empty || !TranslationText(x.Headword, 100) ||
                !TranslationText(x.Meaning, 1000) || !TranslationText(x.PartOfSpeech, 100)) ||
            request.Targets.Select(x => x.Id).Distinct().Count() != request.Targets.Length ||
            request.Targets.Select(x => x.WordId).Distinct().Count() != request.Targets.Length)
            throw new ArgumentException("中翻英需要 1–5 個不同單字及指定詞義。");
    }

    private static bool TranslationText(string? text, int maximum) => !string.IsNullOrWhiteSpace(text) && text.Length <= maximum;
    private static bool TranslationChinese(string? text, int maximum) => TranslationText(text, maximum) && Regex.IsMatch(text!, @"[\u3400-\u9fff]");
    private static bool TranslationEnglish(string? text, int maximum) => TranslationText(text, maximum) &&
        Regex.IsMatch(text!, "[A-Za-z]") && !Regex.IsMatch(text!, @"[\u3400-\u9fff]");

    public static TranslationMaterial ParseTranslationMaterial(string json, TranslationRequest request)
    {
        ValidateTranslationRequest(request);
        var material = JsonSerializer.Deserialize<TranslationMaterial>(json, TranslationJsonOptions)
            ?? throw new InvalidDataException("中翻英題目為空。");
        ValidateTranslationMaterial(material, request);
        return new(request.Targets.Select(target => material.Questions.Single(x => x.SenseId == target.Id)).ToArray());
    }

    private static void ValidateTranslationMaterial(TranslationMaterial material, TranslationRequest request)
    {
        if (material.Questions is null || material.Questions.Length != request.Targets.Length ||
            material.Questions.Any(x => x is null || !TranslationChinese(x.Chinese, 600) || !TranslationEnglish(x.ReferenceEnglish, 1200)) ||
            !material.Questions.Select(x => x.SenseId).Order().SequenceEqual(request.Targets.Select(x => x.Id).Order()) ||
            material.Questions.Select(x => x.Chinese.Trim()).Distinct().Count() != material.Questions.Length)
            throw new InvalidDataException("中翻英題目不完整、重複或目標詞義不符，請重新生成。");
    }

    public static TranslationAssessment ParseTranslationAssessment(string json, TranslationRequest request)
    {
        ValidateTranslationRequest(request);
        var assessment = JsonSerializer.Deserialize<TranslationAssessment>(json, TranslationJsonOptions)
            ?? throw new InvalidDataException("中翻英批改為空。");
        if (assessment.Feedback is null || assessment.Feedback.Length != request.Targets.Length ||
            assessment.Feedback.Any(x => x is null || !Enum.IsDefined(x.Verdict) ||
                !TranslationChinese(x.MeaningFeedback, 2000) || !TranslationChinese(x.TargetFeedback, 2000) ||
                !TranslationChinese(x.GrammarFeedback, 2000) || !TranslationEnglish(x.SuggestedEnglish, 1200)) ||
            !assessment.Feedback.Select(x => x.SenseId).Order().SequenceEqual(request.Targets.Select(x => x.Id).Order()))
            throw new InvalidDataException("中翻英批改不完整或目標詞義不符，請重新提交。");
        return new(request.Targets.Select(target => assessment.Feedback.Single(x => x.SenseId == target.Id)).ToArray());
    }

    private static string TranslationSchema(string template, TranslationRequest request)
    {
        var schema = JsonNode.Parse(template)!;
        var array = schema["properties"]!.AsObject().First().Value!;
        array["minItems"] = request.Targets.Length;
        array["maxItems"] = request.Targets.Length;
        array["items"]!["properties"]!["senseId"]!["enum"] = JsonSerializer.SerializeToNode(request.Targets.Select(x => x.Id.ToString("D")));
        return schema.ToJsonString();
    }

    private const string GenerationSchema = """
    {"type":"object","additionalProperties":false,"required":["questions"],"properties":{
      "questions":{"type":"array","items":{"type":"object","additionalProperties":false,"required":["senseId","chinese","referenceEnglish"],
        "properties":{"senseId":{"type":"string"},"chinese":{"type":"string"},"referenceEnglish":{"type":"string"}}}}}}
    """;
    private const string AssessmentSchema = """
    {"type":"object","additionalProperties":false,"required":["feedback"],"properties":{
      "feedback":{"type":"array","items":{"type":"object","additionalProperties":false,
        "required":["senseId","verdict","meaningFeedback","targetFeedback","grammarFeedback","suggestedEnglish"],"properties":{
        "senseId":{"type":"string"},"verdict":{"type":"string","enum":["Correct","PartlyCorrect","NeedsWork"]},
        "meaningFeedback":{"type":"string"},"targetFeedback":{"type":"string"},"grammarFeedback":{"type":"string"},"suggestedEnglish":{"type":"string"}}}}}}
    """;
}
