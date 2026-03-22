using Newtonsoft.Json;
using System.Collections.Generic;

namespace SSF2ModManager.Models
{
    public class GameBananaMod
    {
        [JsonProperty("_idRow")]
        public int Id { get; set; }

        [JsonProperty("_sModelName")]
        public string ModelName { get; set; } = string.Empty;

        [JsonProperty("_sName")]
        public string Name { get; set; } = string.Empty;

        [JsonProperty("_sDescription")]
        public string Description { get; set; } = string.Empty;

        [JsonProperty("_sText")]
        public string FullText { get; set; } = string.Empty;

        [JsonProperty("_nLikeCount")]
        public int Likes { get; set; }

        [JsonProperty("_nViewCount")]
        public int Views { get; set; }

        [JsonProperty("_nDownloadCount")]
        public int Downloads { get; set; }

        [JsonProperty("_tsDateAdded")]
        public long DateAddedTimestamp { get; set; }

        [JsonProperty("_tsDateUpdated")]
        public long DateUpdatedTimestamp { get; set; }

        [JsonProperty("_aSubmitter")]
        public GameBananaSubmitter? Submitter { get; set; }

        [JsonProperty("_aPreviewMedia")]
        public PreviewMedia? PreviewMedia { get; set; }

        [JsonProperty("_aFiles")]
        public List<GameBananaFile>? Files { get; set; }

        [JsonProperty("_sProfileUrl")]
        public string ProfileUrl { get; set; } = string.Empty;

        [JsonProperty("_aCategory")]
        public GameBananaCategory? Category { get; set; }

        [JsonProperty("_aRootCategory")]
        public GameBananaCategory? RootCategory { get; set; }

        [JsonProperty("_sVersion")]
        public string Version { get; set; } = string.Empty;
    }

    public class GameBananaSubmitter
    {
        [JsonProperty("_idRow")]
        public int Id { get; set; }

        [JsonProperty("_sName")]
        public string Name { get; set; } = string.Empty;

        [JsonProperty("_sAvatarUrl")]
        public string AvatarUrl { get; set; } = string.Empty;

        [JsonProperty("_sProfileUrl")]
        public string ProfileUrl { get; set; } = string.Empty;
    }

    public class PreviewMedia
    {
        [JsonProperty("_aImages")]
        public List<GameBananaImage>? Images { get; set; }
    }

    public class GameBananaImage
    {
        [JsonProperty("_sBaseUrl")]
        public string BaseUrl { get; set; } = string.Empty;

        [JsonProperty("_sFile")]
        public string File { get; set; } = string.Empty;

        [JsonProperty("_sFile100")]
        public string File100 { get; set; } = string.Empty;

        [JsonProperty("_sFile220")]
        public string File220 { get; set; } = string.Empty;

        [JsonProperty("_sCaption")]
        public string Caption { get; set; } = string.Empty;

        public string ThumbnailUrl => string.IsNullOrEmpty(File220) 
            ? $"{BaseUrl}/{File}" 
            : $"{BaseUrl}/{File220}";

        public string FullUrl => $"{BaseUrl}/{File}";
    }

    public class GameBananaFile
    {
        [JsonProperty("_idRow")]
        public int Id { get; set; }

        [JsonProperty("_sFile")]
        public string FileName { get; set; } = string.Empty;

        [JsonProperty("_nFilesize")]
        public long FileSize { get; set; }

        [JsonProperty("_sDownloadUrl")]
        public string DownloadUrl { get; set; } = string.Empty;

        [JsonProperty("_sDescription")]
        public string Description { get; set; } = string.Empty;

        [JsonProperty("_tsDateAdded")]
        public long DateAdded { get; set; }

        [JsonProperty("_sAnalysisState")]
        public string AnalysisState { get; set; } = string.Empty;

        [JsonProperty("_sAnalysisResult")]
        public string AnalysisResult { get; set; } = string.Empty;

        [JsonProperty("_sAnalysisResultVerbose")]
        public string AnalysisResultVerbose { get; set; } = string.Empty;

        [JsonProperty("_sAvState")]
        public string AvState { get; set; } = string.Empty;

        [JsonProperty("_sAvResult")]
        public string AvResult { get; set; } = string.Empty;

        [JsonProperty("_sMd5Checksum")]
        public string Md5Checksum { get; set; } = string.Empty;

        public string ScanSummary
        {
            get
            {
                var parts = new List<string>();
                if (!string.IsNullOrEmpty(AnalysisResult))
                    parts.Add($"Analysis: {AnalysisResult}");
                if (!string.IsNullOrEmpty(AnalysisResultVerbose))
                    parts.Add(AnalysisResultVerbose);
                if (!string.IsNullOrEmpty(AvResult))
                    parts.Add($"Antivirus: {AvResult}");
                return parts.Count > 0 ? string.Join(" | ", parts) : "No scan data";
            }
        }

        public bool HasScanWarning =>
            (!string.IsNullOrEmpty(AnalysisResult) && !AnalysisResult.Equals("ok", StringComparison.OrdinalIgnoreCase)) ||
            (!string.IsNullOrEmpty(AvResult) && !AvResult.Equals("clean", StringComparison.OrdinalIgnoreCase));

        public string FileSizeFormatted
        {
            get
            {
                if (FileSize < 1024) return $"{FileSize} B";
                if (FileSize < 1024 * 1024) return $"{FileSize / 1024.0:F1} KB";
                return $"{FileSize / (1024.0 * 1024.0):F1} MB";
            }
        }
    }

    public class GameBananaCategory
    {
        [JsonProperty("_sName")]
        public string Name { get; set; } = string.Empty;

        [JsonProperty("_sProfileUrl")]
        public string ProfileUrl { get; set; } = string.Empty;
    }

    public class GameBananaSearchResult
    {
        [JsonProperty("_aRecords")]
        public List<GameBananaMod>? Records { get; set; }

        [JsonProperty("_nRecordCount")]
        public int TotalCount { get; set; }
    }
}
