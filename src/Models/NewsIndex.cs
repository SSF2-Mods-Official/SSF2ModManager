using System.Collections.Generic;

namespace SSF2ModManager.Models
{
    public class NewsIndex
    {
        public int Version { get; set; } = 1;
        public string Updated { get; set; } = "";
        public List<NewsIndexEntry> Articles { get; set; } = new();
    }

    public class NewsIndexEntry
    {
        public string Id { get; set; } = "";
        public string Path { get; set; } = "";
        public string Title { get; set; } = "";
        public string Date { get; set; } = "";
        public List<string> Tags { get; set; } = new();
        public bool Draft { get; set; }
        public List<string> Assets { get; set; } = new();
    }

    public class NewsSyncResult
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public int DownloadedCount { get; set; }
    }
}
