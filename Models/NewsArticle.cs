using System;
using System.Collections.Generic;

namespace SSF2ModManager.Models
{
    public class NewsArticle
    {
        public string Title { get; set; } = "";
        public DateTime Date { get; set; }
        public string Author { get; set; } = "";
        public string Excerpt { get; set; } = "";
        public string FeaturedImage { get; set; } = ""; // relative path
        public List<string> Tags { get; set; } = new();
        public bool Draft { get; set; } = false;

        // The converted HTML content (full HTML document including base href)
        public string Html { get; set; } = "";

        // The original markdown source (for FlowDocument rendering)
        public string RawMarkdown { get; set; } = "";

        // Source folder for resolving relative assets
        public string SourceFolder { get; set; } = "";
    }
}
