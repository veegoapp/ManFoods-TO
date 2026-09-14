namespace MvcApp.Models.ViewModels
{
    /// <summary>One expandable entry inside a page guide (usually one chart).</summary>
    public class PageGuideSection
    {
        public string Icon { get; set; } = "bi-bar-chart";
        public string Title { get; set; } = "";
        public string Body { get; set; } = "";
        /// <summary>When true, Body is rendered as raw HTML (e.g. a reference table)
        /// instead of encoded plain text. Only set this for static, developer-authored
        /// resx content — never for anything derived from user input.</summary>
        public bool IsHtml { get; set; } = false;
    }

    /// <summary>Content for the "page guide" drawer rendered by _PageGuide.cshtml.</summary>
    public class PageGuideViewModel
    {
        /// <summary>Unique key used to namespace element ids (e.g. "turnover").</summary>
        public string PageKey { get; set; } = "";
        public string Overview { get; set; } = "";
        public List<PageGuideSection> Sections { get; set; } = new();
        public string? FilterNote { get; set; }
    }
}
