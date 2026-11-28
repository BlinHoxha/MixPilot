namespace MixPilot.Contracts;

public static class TrackTags
{
    public static bool TryNormalize(IEnumerable<string> tags, out string[] normalized, out string? error)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var value in tags)
        {
            string tag = string.Join(' ', (value ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
            if (tag.Length == 0) continue;
            if (tag.Length > 40 || tag.Contains('|'))
            {
                normalized = []; error = "Tags must contain at most 40 characters each and cannot contain |.";
                return false;
            }
            if (seen.Add(tag)) result.Add(tag);
        }
        normalized = result.ToArray();
        error = result.Count > 20 ? "At most 20 distinct tags are allowed." : null;
        return error is null;
    }
}
