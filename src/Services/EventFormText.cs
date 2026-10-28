using System.Collections.Generic;

namespace SSF2ModManager.Services
{
    public sealed class EventFormPick
    {
        public EventFormPick(string id, string label)
        {
            Id = id ?? "";
            Label = string.IsNullOrWhiteSpace(label) ? Id : label;
        }

        public string Id { get; }
        public string Label { get; }

        public override string ToString() => Label;
    }

    /// <summary>Record cuts for the event form. Time is stored as frames at 60 fps.</summary>
    public static class EventRankText
    {
        public const int Fps = 60;

        public static bool IsTime(string? measure) =>
            measure is "framesAtMost" or "framesAtLeast" or "perMatch";

        public static int[] Defaults(string? measure) => measure switch
        {
            "framesAtMost" => new[] { 2 * 60 * Fps, 3 * 60 * Fps, 4 * 60 * Fps, 5 * 60 * Fps, 6 * 60 * Fps, 8 * 60 * Fps },
            "framesAtLeast" => new[] { 30 * Fps, 60 * Fps, 90 * Fps, 2 * 60 * Fps, 3 * 60 * Fps, 4 * 60 * Fps },
            "damageAtMost" => new[] { 20, 40, 60, 80, 120, 160 },
            "damageAtLeast" => new[] { 50, 100, 150, 200, 300, 400 },
            "countAtLeast" => new[] { 2, 4, 6, 8, 10, 15 },
            "perMatch" => new[] { 300, 550, 900, 1300, 1600, 2500 },
            _ => new[] { 0, 0, 0, 0, 0, 0 }
        };

        public static string Show(int value, bool clock)
        {
            if (!clock)
                return value.ToString();
            if (value < 0)
                value = 0;
            var seconds = value / Fps;
            return (seconds / 60) + ":" + (seconds % 60).ToString("00");
        }

        public static int Read(string? text, bool clock)
        {
            text = (text ?? "").Trim();
            if (clock && text.Contains(':'))
            {
                var parts = text.Split(':');
                if (parts.Length == 2
                    && int.TryParse(parts[0], out var minutes)
                    && int.TryParse(parts[1], out var seconds))
                    return (minutes * 60 + seconds) * Fps;
            }
            return int.TryParse(text, out var n) ? n : 0;
        }
    }

    public readonly struct EventItemRow
    {
        public string Id { get; init; }
        public bool On { get; init; }
        public bool WasBanned { get; init; }
        public bool Touched { get; init; }
    }

    /// <summary>One item checkbox. On is required. Off is banned only when the event already banned it, the box was clicked, or only the checked items should remain.</summary>
    public static class EventItemList
    {
        public static void Split(IReadOnlyList<EventItemRow> rows, bool onlyChecked, out List<string> required, out List<string> banned)
        {
            required = new List<string>();
            banned = new List<string>();
            if (rows == null)
                return;
            foreach (var row in rows)
            {
                if (string.IsNullOrWhiteSpace(row.Id))
                    continue;
                if (row.On)
                {
                    required.Add(row.Id);
                    continue;
                }
                if (onlyChecked || row.WasBanned || row.Touched)
                    banned.Add(row.Id);
            }
        }
    }
}
