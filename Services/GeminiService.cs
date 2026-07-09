using System.Text;
using System.Text.Json;

namespace MvcApp.Services;

/// <summary>
/// Implements IGeminiService using OpenRouter API (OpenAI-compatible).
/// Set OPENROUTER_API_KEY in Replit Secrets.
/// </summary>
public class GeminiService : IGeminiService
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<GeminiService> _logger;

    private const string OpenRouterUrl  = "https://openrouter.ai/api/v1/chat/completions";
    private const string DefaultModel   = "meta-llama/llama-3.3-70b-instruct:free";

    public GeminiService(IHttpClientFactory httpFactory, ILogger<GeminiService> logger)
    {
        _httpFactory = httpFactory;
        _logger      = logger;
    }

    public async Task<GeminiAnswer> AskAsync(string userQuestion, GeminiContext ctx)
    {
        var apiKey = Environment.GetEnvironmentVariable("OPENROUTER_API_KEY");
        if (string.IsNullOrEmpty(apiKey))
            return new GeminiAnswer { Text = "⚠️ AI غير مُفعّل. تأكد من إعداد OPENROUTER_API_KEY في الـ Secrets." };

        var systemPrompt = BuildSystemPrompt(ctx, userQuestion);

        var requestBody = new
        {
            model    = DefaultModel,
            messages = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user",   content = userQuestion }
            },
            max_tokens  = 1024,
            temperature = 0.7
        };

        try
        {
            var client  = _httpFactory.CreateClient();
            var json    = JsonSerializer.Serialize(requestBody);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            using var request = new HttpRequestMessage(HttpMethod.Post, OpenRouterUrl) { Content = content };
            request.Headers.Add("Authorization", $"Bearer {apiKey}");
            request.Headers.Add("HTTP-Referer", "https://manfoodsto.replit.app");
            request.Headers.Add("X-Title", "ManFoodsTO Workforce Intelligence");

            var response     = await client.SendAsync(request);
            var responseJson = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("OpenRouter API error {Status}: {Body}", response.StatusCode, responseJson);
                var errorText = (int)response.StatusCode switch
                {
                    401 => "⚠️ الـ API Key غير صحيح أو منتهي الصلاحية. تأكد من OPENROUTER_API_KEY.",
                    402 => "⚠️ رصيد OpenRouter غير كافٍ.",
                    429 => "⚠️ تم تجاوز حد الطلبات. انتظر دقيقة وحاول مجدداً.",
                    _   => $"⚠️ خطأ في الاتصال بالـ AI (كود {(int)response.StatusCode}). حاول مرة أخرى.",
                };
                return new GeminiAnswer { Text = errorText };
            }

            using var doc = JsonDocument.Parse(responseJson);

            var promptTokens     = 0;
            var completionTokens = 0;
            if (doc.RootElement.TryGetProperty("usage", out var usage))
            {
                if (usage.TryGetProperty("prompt_tokens",     out var pt)) promptTokens     = pt.GetInt32();
                if (usage.TryGetProperty("completion_tokens", out var ct)) completionTokens = ct.GetInt32();
            }

            if (!doc.RootElement.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0)
                return new GeminiAnswer { Text = "⚠️ لم يُرجع الـ AI أي إجابة. حاول إعادة صياغة السؤال.", PromptTokens = promptTokens, CompletionTokens = completionTokens };

            var text = choices[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString();

            return new GeminiAnswer
            {
                Text             = text ?? "⚠️ لم يتم الحصول على إجابة.",
                PromptTokens     = promptTokens,
                CompletionTokens = completionTokens
            };
        }
        catch (TaskCanceledException)
        {
            _logger.LogWarning("OpenRouter API timeout");
            return new GeminiAnswer { Text = "⚠️ انتهت مهلة الاتصال بالـ AI. تأكد من اتصالك بالإنترنت وحاول مجدداً." };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OpenRouter service exception");
            return new GeminiAnswer { Text = "⚠️ حدث خطأ غير متوقع أثناء الاتصال بالـ AI. حاول مرة أخرى." };
        }
    }

    // ─── Prompt Builder ────────────────────────────────────────────────────────

    private static string BuildSystemPrompt(GeminiContext ctx, string userQuestion)
    {
        var periodLabel = (ctx.Month.HasValue && ctx.Year.HasValue)
            ? $"{ctx.Month}/{ctx.Year}"
            : "غير محدد";

        var storeLabel = string.IsNullOrEmpty(ctx.Store) ? "جميع الفروع" : ctx.Store;

        var storeTable = new StringBuilder();
        if (ctx.StoreBreakdowns.Count > 0)
        {
            storeTable.AppendLine("=== تفصيل الـ Turnover بالفروع (مرتب تنازلياً) ===");
            storeTable.AppendLine($"{"الفرع",-30} {"Headcount",10} {"New Hires",10} {"استقالات",10} {"Turnover%",10}");
            storeTable.AppendLine(new string('-', 70));
            foreach (var s in ctx.StoreBreakdowns)
                storeTable.AppendLine($"{s.Store,-30} {s.Headcount,10} {s.NewHires,10} {s.Resignations,10} {s.TurnoverRate,9:F1}%");
            storeTable.AppendLine();
        }

        var jobTitleSection = new StringBuilder();
        if (ctx.TurnoverByJobTitle.Count > 0)
        {
            jobTitleSection.AppendLine("=== الاستقالات حسب المسمى الوظيفي ===");
            foreach (var (label, value) in ctx.TurnoverByJobTitle)
                jobTitleSection.AppendLine($"  • {label}: {value}");
            jobTitleSection.AppendLine();
        }

        var tenureSection = new StringBuilder();
        if (ctx.TurnoverByTenure.Count > 0)
        {
            tenureSection.AppendLine("=== الاستقالات حسب مدة الخدمة ===");
            foreach (var (label, value) in ctx.TurnoverByTenure)
                tenureSection.AppendLine($"  • {label}: {value}");
            tenureSection.AppendLine();
        }

        var genderSection = new StringBuilder();
        if (ctx.GenderBreakdown.Count > 0)
        {
            genderSection.AppendLine("=== توزيع الموظفين حسب الجنس ===");
            foreach (var (label, value) in ctx.GenderBreakdown)
                genderSection.AppendLine($"  • {label}: {value}");
            genderSection.AppendLine();
        }

        var retentionSection = new StringBuilder();
        if (ctx.RetentionMilestones.Count > 0)
        {
            retentionSection.AppendLine("=== نسبة الاحتفاظ بالموظفين حسب المدة منذ التعيين ===");
            foreach (var (label, rate) in ctx.RetentionMilestones)
                retentionSection.AppendLine($"  • بعد {label}: {rate:F1}% لسه شغالين");
            retentionSection.AppendLine();
        }

        var ninetyDaySection = new StringBuilder();
        if (ctx.NinetyDayCohorts.Count > 0)
        {
            ninetyDaySection.AppendLine("=== نسبة ترك العمل خلال أول 90 يوم، لكل شهر تعيين ===");
            foreach (var (label, rate, provisional) in ctx.NinetyDayCohorts)
                ninetyDaySection.AppendLine($"  • {label}: {rate:F1}%{(provisional ? " (لسه تحت المراجعة)" : "")}");
            ninetyDaySection.AppendLine();
        }

        var exitReasonsSection = new StringBuilder();
        if (ctx.ExitInterviewReasons.Count > 0)
        {
            exitReasonsSection.AppendLine("=== أكتر أسباب ترك العمل في مقابلات الخروج ===");
            foreach (var (reason, count) in ctx.ExitInterviewReasons)
                exitReasonsSection.AppendLine($"  • {reason}: {count} حالة");
            exitReasonsSection.AppendLine();
        }

        return $"""
            أنت مساعد HR ذكي متخصص في تحليل بيانات الموارد البشرية لمنصة Workforce Intelligence – Crew Level.
            مهمتك هي الإجابة على أسئلة المدراء بناءً على البيانات المتاحة فقط.

            === الملخص العام ===
            الفترة: {periodLabel}
            الفرع المختار: {storeLabel}
            إجمالي الموظفين (Headcount): {ctx.TotalHeadcount}
            الموظفون الجدد (New Hires): {ctx.NewHires}
            إجمالي الاستقالات: {ctx.TotalResignations}
            معدل الـ Turnover الإجمالي: {ctx.TurnoverRate}%
            ====================

            {storeTable}
            {jobTitleSection}
            {tenureSection}
            {genderSection}
            {retentionSection}
            {ninetyDaySection}
            {exitReasonsSection}

            قواعد مهمة:
            - أجب دائمًا بشكل موجز ومفيد.
            - استند فقط إلى الأرقام الموجودة في البيانات أعلاه — لا تخترع أرقامًا.
            - إذا سُئلت عن بيانات غير متاحة، اذكر ذلك بوضوح.
            - يمكنك الإجابة بالعربية أو الإنجليزية حسب لغة السؤال.
            - قدّم توصيات عملية عند الطلب.
            - عند الإجابة عن "أعلى فرع turnover"، استخدم جدول الفروع مباشرة.
            - بيانات الاحتفاظ وأول 90 يوم ومقابلات الخروج على مستوى الشركة كلها عبر كل الفترات.
            """;
    }
}
