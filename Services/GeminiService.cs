using System.Text;
using System.Text.Json;

namespace MvcApp.Services;

public class GeminiService : IGeminiService
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<GeminiService> _logger;

    public GeminiService(IHttpClientFactory httpFactory, ILogger<GeminiService> logger)
    {
        _httpFactory = httpFactory;
        _logger = logger;
    }

    public async Task<GeminiAnswer> AskAsync(string userQuestion, GeminiContext ctx)
    {
        var apiKey = Environment.GetEnvironmentVariable("Gemini_API_Key");
        if (string.IsNullOrEmpty(apiKey))
            return new GeminiAnswer { Text = "AI غير مُفعّل. تأكد من إعداد Gemini_API_Key في الـ Secrets." };

        var periodLabel = (ctx.Month.HasValue && ctx.Year.HasValue)
            ? $"{ctx.Month}/{ctx.Year}"
            : "غير محدد";

        var storeLabel = string.IsNullOrEmpty(ctx.Store) ? "جميع الفروع" : ctx.Store;

        // Build per-store breakdown table
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

        // Build job-title breakdown
        var jobTitleSection = new StringBuilder();
        if (ctx.TurnoverByJobTitle.Count > 0)
        {
            jobTitleSection.AppendLine("=== الاستقالات حسب المسمى الوظيفي ===");
            foreach (var (label, value) in ctx.TurnoverByJobTitle)
                jobTitleSection.AppendLine($"  • {label}: {value}");
            jobTitleSection.AppendLine();
        }

        // Build tenure breakdown
        var tenureSection = new StringBuilder();
        if (ctx.TurnoverByTenure.Count > 0)
        {
            tenureSection.AppendLine("=== الاستقالات حسب مدة الخدمة ===");
            foreach (var (label, value) in ctx.TurnoverByTenure)
                tenureSection.AppendLine($"  • {label}: {value}");
            tenureSection.AppendLine();
        }

        // Build gender breakdown
        var genderSection = new StringBuilder();
        if (ctx.GenderBreakdown.Count > 0)
        {
            genderSection.AppendLine("=== توزيع الموظفين حسب الجنس ===");
            foreach (var (label, value) in ctx.GenderBreakdown)
                genderSection.AppendLine($"  • {label}: {value}");
            genderSection.AppendLine();
        }

        // Build retention milestones (company-wide, all cohorts to date)
        var retentionSection = new StringBuilder();
        if (ctx.RetentionMilestones.Count > 0)
        {
            retentionSection.AppendLine("=== نسبة الاحتفاظ بالموظفين حسب المدة منذ التعيين (كل الكوهورتات) ===");
            foreach (var (label, rate) in ctx.RetentionMilestones)
                retentionSection.AppendLine($"  • بعد {label}: {rate:F1}% لسه شغالين");
            retentionSection.AppendLine();
        }

        // Build 90-day early-leaver trend by cohort month
        var ninetyDaySection = new StringBuilder();
        if (ctx.NinetyDayCohorts.Count > 0)
        {
            ninetyDaySection.AppendLine("=== نسبة ترك العمل خلال أول 90 يوم، لكل شهر تعيين ===");
            foreach (var (label, rate, provisional) in ctx.NinetyDayCohorts)
                ninetyDaySection.AppendLine($"  • {label}: {rate:F1}%{(provisional ? " (لسه تحت المراجعة، ممكن تتغير)" : "")}");
            ninetyDaySection.AppendLine();
        }

        // Build exit interview reasons (never includes names or IDs)
        var exitReasonsSection = new StringBuilder();
        if (ctx.ExitInterviewReasons.Count > 0)
        {
            exitReasonsSection.AppendLine("=== أكتر أسباب ترك العمل ذكرًا في مقابلات الخروج ===");
            foreach (var (reason, count) in ctx.ExitInterviewReasons)
                exitReasonsSection.AppendLine($"  • {reason}: {count} حالة");
            exitReasonsSection.AppendLine();
        }

        var systemPrompt = $"""
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
            - بيانات الاحتفاظ وأول 90 يوم ومقابلات الخروج دي على مستوى الشركة كلها عبر كل الفترات، مش مقتصرة على الفترة/الفرع المختار فوق.

            سؤال المستخدم: {userQuestion}
            """;

        var requestBody = new
        {
            contents = new[]
            {
                new
                {
                    parts = new[]
                    {
                        new { text = systemPrompt }
                    }
                }
            },
            generationConfig = new
            {
                temperature = 0.7,
                maxOutputTokens = 1024
            }
        };

        try
        {
            var client = _httpFactory.CreateClient();
            const string url = "https://generativelanguage.googleapis.com/v1beta/models/gemini-1.5-flash:generateContent";
            var json = JsonSerializer.Serialize(requestBody);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };
            request.Headers.Add("x-goog-api-key", apiKey); // keep key out of URL / logs

            var response = await client.SendAsync(request);
            var responseJson = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Gemini API error {Status}: {Body}", response.StatusCode, responseJson);
                var errorText = (int)response.StatusCode switch
                {
                    403 => "⚠️ الـ API Key مش عنده صلاحية على هذا الـ model. تأكد إن الـ Gemini_API_Key صحيح وفعّال من Google AI Studio.",
                    429 => "⚠️ تم تجاوز حد الطلبات على الـ AI. انتظر دقيقة وحاول مجدداً.",
                    400 => "⚠️ طلب غير صحيح. حاول إعادة صياغة السؤال.",
                    _   => $"⚠️ خطأ في الاتصال بالـ AI (كود {(int)response.StatusCode}). حاول مرة أخرى.",
                };
                return new GeminiAnswer { Text = errorText };
            }

            using var doc = JsonDocument.Parse(responseJson);

            var promptTokens = 0;
            var completionTokens = 0;
            if (doc.RootElement.TryGetProperty("usageMetadata", out var usage))
            {
                if (usage.TryGetProperty("promptTokenCount", out var pt)) promptTokens = pt.GetInt32();
                if (usage.TryGetProperty("candidatesTokenCount", out var ct)) completionTokens = ct.GetInt32();
            }

            if (!doc.RootElement.TryGetProperty("candidates", out var candidates) || candidates.GetArrayLength() == 0)
                return new GeminiAnswer { Text = "⚠️ لم يُرجع الـ AI أي إجابة. حاول إعادة صياغة السؤال.", PromptTokens = promptTokens, CompletionTokens = completionTokens };

            var text = candidates[0]
                .GetProperty("content")
                .GetProperty("parts")[0]
                .GetProperty("text")
                .GetString();

            return new GeminiAnswer { Text = text ?? "⚠️ لم يتم الحصول على إجابة.", PromptTokens = promptTokens, CompletionTokens = completionTokens };
        }
        catch (TaskCanceledException)
        {
            _logger.LogWarning("Gemini API timeout");
            return new GeminiAnswer { Text = "⚠️ انتهت مهلة الاتصال بالـ AI. تأكد من اتصالك بالإنترنت وحاول مجدداً." };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Gemini service exception");
            return new GeminiAnswer { Text = "⚠️ حدث خطأ غير متوقع أثناء الاتصال بالـ AI. حاول مرة أخرى." };
        }
    }
}
