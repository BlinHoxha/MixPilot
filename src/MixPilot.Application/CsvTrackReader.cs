using System.ComponentModel.DataAnnotations;
using System.Globalization;
using Microsoft.VisualBasic.FileIO;
using MixPilot.Contracts;
using MixPilot.Domain;

namespace MixPilot.Application;

internal static class CsvTrackReader
{
    public static IReadOnlyList<CreateTrackRequest> Read(string csv)
    {
        using var parser = new TextFieldParser(new StringReader(csv)) { HasFieldsEnclosedInQuotes = true, TrimWhiteSpace = true };
        parser.SetDelimiters(",");
        var output = new List<CreateTrackRequest>();
        try
        {
            var header = parser.ReadFields() ?? throw Invalid("CSV must contain a header row.");
            header[0] = header[0].TrimStart('\uFEFF');
            if (header.Distinct(StringComparer.OrdinalIgnoreCase).Count() != header.Length) throw Invalid("CSV headers must be unique.");
            string[] allowed = ["artist", "title", "version", "durationSeconds", "bpm", "musicalKey", "energy", "genre", "mood", "hasVocals",
                "album", "label", "releaseDate", "subgenre", "tags"];
            if (header.Any(h => !allowed.Contains(h, StringComparer.OrdinalIgnoreCase))) throw Invalid("CSV contains an unsupported column.");
            foreach (var required in new[] { "artist", "title", "durationSeconds" })
                if (!header.Contains(required, StringComparer.OrdinalIgnoreCase)) throw Invalid($"CSV requires the {required} column.");
            while (!parser.EndOfData)
            {
                if (output.Count >= 500) throw Invalid("A CSV import is limited to 500 tracks.");
                var row = parser.ReadFields()!;
                if (row.Length != header.Length) throw Invalid($"CSV record {output.Count + 2} has an unexpected column count.");
                var fields = header.Zip(row).ToDictionary(p => p.First, p => p.Second, StringComparer.OrdinalIgnoreCase);
                string Value(string key) => fields.GetValueOrDefault(key, "");
                string? Optional(string key) => string.IsNullOrWhiteSpace(Value(key)) ? null : Value(key);
                DateOnly? releaseDate = null;
                if (Optional("releaseDate") is { } dateText)
                {
                    if (!DateOnly.TryParseExact(dateText, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
                        throw Invalid($"CSV record {output.Count + 2}: releaseDate must be a valid yyyy-MM-dd date.");
                    releaseDate = parsed;
                }
                var request = new CreateTrackRequest
                {
                    Artist = Value("artist"), Title = Value("title"), Version = Value("version"),
                    DurationSeconds = int.Parse(Value("durationSeconds"), CultureInfo.InvariantCulture),
                    Bpm = Optional("bpm") is { } bpm ? decimal.Parse(bpm, CultureInfo.InvariantCulture) : null,
                    MusicalKey = Optional("musicalKey"),
                    Energy = Optional("energy") is { } energy ? int.Parse(energy, CultureInfo.InvariantCulture) : null,
                    Genre = Value("genre"), Mood = Value("mood"),
                    HasVocals = Optional("hasVocals") is { } vocals ? bool.Parse(vocals) : null,
                    Album = Optional("album"), Label = Optional("label"), ReleaseDate = releaseDate,
                    Subgenre = Optional("subgenre"), Tags = Optional("tags")?.Split(';'),
                    AuthorizationConfirmed = true
                };
                var results = new List<ValidationResult>();
                if (!Validator.TryValidateObject(request, new ValidationContext(request), results, true))
                    throw Invalid($"CSV record {output.Count + 2}: {string.Join(" ", results.Select(r => r.ErrorMessage))}");
                output.Add(request);
            }
        }
        catch (Exception ex) when (ex is FormatException or OverflowException or MalformedLineException)
        { throw Invalid($"CSV record {output.Count + 2} is malformed or contains an invalid numeric/boolean value."); }
        if (output.Count == 0) throw Invalid("CSV must contain at least one track.");
        return output;
    }

    private static RuleViolation Invalid(string message) => new("invalid_csv", message, 400);
}
