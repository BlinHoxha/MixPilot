using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Framework.Contracts.Common;
using Framework.Contracts.Pagination;
using MixPilot.Contracts;
using MixPilot.Domain;

namespace MixPilot.Tests;

public sealed class TrackMetadataTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    { Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) } };

    private static async Task<T> Data<T>(HttpResponseMessage response)
    {
        Assert.True(response.IsSuccessStatusCode, $"{response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        return (await response.Content.ReadFromJsonAsync<CommonResult<T>>(Json))!.Data!;
    }

    private static bool Valid(TrackWriteRequest request) =>
        Validator.TryValidateObject(request, new ValidationContext(request), [], true);

    [Fact]
    public void Tags_normalize_whitespace_deduplicate_in_order_and_roundtrip_storage()
    {
        Assert.True(TrackTags.TryNormalize(["  Deep\t House ", "", " \n ", "deep house", "Peak", "PEAK"], out var tags, out _));
        Assert.Equal(new[] { "Deep House", "Peak" }, tags);
        var track = new Track { Tags = tags };
        Assert.Equal("|Deep House|Peak|", track.TagsStorage);
        Assert.Equal(tags, track.Tags);
        track.Tags = [];
        Assert.Equal("", track.TagsStorage);
        Assert.Empty(track.Tags);
        Assert.True(TrackTags.TryNormalize(Enumerable.Repeat("same", 21), out tags, out _));
        Assert.Single(tags);
        Assert.True(TrackTags.TryNormalize(Enumerable.Range(1, 20).Select(i => $"tag {i}"), out _, out _));
        Assert.False(TrackTags.TryNormalize(Enumerable.Range(1, 21).Select(i => $"tag {i}"), out _, out _));
        Assert.True(TrackTags.TryNormalize([new string('a', 40)], out _, out _));
        Assert.False(TrackTags.TryNormalize([new string('a', 41)], out _, out _));
        Assert.False(TrackTags.TryNormalize(["bad|tag"], out _, out _));
    }

    [Fact]
    public void Metadata_validates_lengths_dates_and_clear_conflicts_for_create_and_update()
    {
        Assert.True(Valid(new CreateTrackRequest
        {
            Artist = "A", Title = "B", DurationSeconds = 300, AuthorizationConfirmed = true,
            Album = new string('a', 200), Label = new string('l', 200), Subgenre = new string('s', 100),
            ReleaseDate = new DateOnly(1900, 1, 1), Tags = [new string('t', 40)]
        }));
        var tomorrowYear = DateOnly.FromDateTime(DateTime.UtcNow).AddYears(1);
        Assert.True(Valid(new UpdateTrackRequest { Artist = "A", Title = "B", DurationSeconds = 300, ExpectedRevision = 1, ReleaseDate = tomorrowYear }));
        foreach (var request in new TrackWriteRequest[]
        {
            new() { Album = new string('a', 201) }, new() { Label = new string('l', 201) },
            new() { Subgenre = new string('s', 101) }, new() { ReleaseDate = new DateOnly(1899, 12, 31) },
            new() { ReleaseDate = tomorrowYear.AddDays(1) }, new() { ReleaseDate = tomorrowYear, ClearReleaseDate = true },
            new() { Tags = ["invalid|tag"] }
        })
        {
            // Validate only the new metadata; the required legacy properties are supplied below.
            var update = new UpdateTrackRequest { Artist = "A", Title = "B", DurationSeconds = 300, ExpectedRevision = 1,
                Album = request.Album, Label = request.Label, Subgenre = request.Subgenre,
                ReleaseDate = request.ReleaseDate, ClearReleaseDate = request.ClearReleaseDate, Tags = request.Tags };
            Assert.False(Valid(update));
        }
        Assert.False(Valid(new CreateTrackRequest { Artist = "A", Title = "B", DurationSeconds = 300, AuthorizationConfirmed = true, Tags = ["bad|tag"] }));
    }

    [Fact]
    public async Task Api_roundtrips_preserves_omitted_metadata_and_explicitly_clears_it()
    {
        using var client = factory.Client(Guid.NewGuid().ToString());
        var created = await Data<TrackResponse>(await client.PostAsJsonAsync("/api/v1/tracks", new CreateTrackRequest
        {
            Artist = "A", Title = "Metadata", DurationSeconds = 300, AuthorizationConfirmed = true,
            Album = " Album ", Label = "Label", ReleaseDate = new DateOnly(2020, 2, 29), Subgenre = "Deep House",
            Tags = ["  Late\tNight ", "late night", "Warm"]
        }));
        Assert.Equal("Album", created.Album); Assert.Equal("Label", created.Label);
        Assert.Equal(new DateOnly(2020, 2, 29), created.ReleaseDate); Assert.Equal("Deep House", created.Subgenre);
        Assert.Equal(new[] { "Late Night", "Warm" }, created.Tags);
        var preserved = await Data<TrackResponse>(await client.PutAsJsonAsync($"/api/v1/tracks/{created.Id}",
            new { artist = "A", title = "Metadata", durationSeconds = 300, expectedRevision = 1 }));
        Assert.Equal(created.Album, preserved.Album); Assert.Equal(created.Label, preserved.Label);
        Assert.Equal(created.ReleaseDate, preserved.ReleaseDate); Assert.Equal(created.Subgenre, preserved.Subgenre);
        Assert.Equal(created.Tags, preserved.Tags);
        using var other = factory.Client(Guid.NewGuid().ToString());
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/v1/tracks/{created.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.PutAsJsonAsync($"/api/v1/tracks/{created.Id}",
            new { artist = "A", title = "Metadata", durationSeconds = 300, expectedRevision = 2, album = "Attempt" })).StatusCode);
        var cleared = await Data<TrackResponse>(await client.PutAsJsonAsync($"/api/v1/tracks/{created.Id}", new UpdateTrackRequest
        { Artist = "A", Title = "Metadata", DurationSeconds = 300, ExpectedRevision = 2, Album = "", Label = "", Subgenre = "", Tags = [], ClearReleaseDate = true }));
        Assert.Equal("", cleared.Album); Assert.Equal("", cleared.Label); Assert.Equal("", cleared.Subgenre);
        Assert.Empty(cleared.Tags); Assert.Null(cleared.ReleaseDate);
        var defaults = await client.PostAsJsonAsync("/api/v1/tracks", new CreateTrackRequest
        { Artist = "A", Title = "Defaults", DurationSeconds = 300, AuthorizationConfirmed = true });
        var body = (await defaults.Content.ReadFromJsonAsync<JsonObject>())!["data"]!;
        Assert.Equal("", body["album"]!.GetValue<string>()); Assert.Equal("", body["label"]!.GetValue<string>());
        Assert.Equal("", body["subgenre"]!.GetValue<string>()); Assert.Empty(body["tags"]!.AsArray());
        Assert.True(body.AsObject().ContainsKey("releaseDate")); Assert.Null(body["releaseDate"]);
    }

    [Fact]
    public async Task Search_translates_metadata_filters_and_null_last_date_sort_with_pagination()
    {
        using var client = factory.Client(Guid.NewGuid().ToString());
        foreach (var request in new[]
        {
            new CreateTrackRequest { Artist = "A", Title = "Early", DurationSeconds = 300, AuthorizationConfirmed = true,
                Album = "Needle Album", Subgenre = "Deep House", Tags = ["Late Night"], ReleaseDate = new DateOnly(2000, 1, 1) },
            new CreateTrackRequest { Artist = "A", Title = "Late", DurationSeconds = 300, AuthorizationConfirmed = true,
                Label = "Needle Label", Subgenre = "deep house", Tags = ["Late Night Extended"], ReleaseDate = new DateOnly(2020, 1, 1) },
            new CreateTrackRequest { Artist = "A", Title = "Undated", DurationSeconds = 300, AuthorizationConfirmed = true, Tags = ["Late Night"] }
        }) await Data<TrackResponse>(await client.PostAsJsonAsync("/api/v1/tracks", request));
        var search = await Data<PageResponse<TrackResponse>>(await client.GetAsync("/api/v1/tracks?search=needle&pageSize=1"));
        Assert.Equal(2, search.TotalCount); Assert.Single(search.Items); Assert.Equal(2, search.TotalPages);
        var subgenre = await Data<PageResponse<TrackResponse>>(await client.GetAsync("/api/v1/tracks?subgenre=DEEP%20HOUSE"));
        Assert.Equal(2, subgenre.TotalCount);
        var tags = await Data<PageResponse<TrackResponse>>(await client.GetAsync("/api/v1/tracks?tag=late%20%20night&sortBy=releaseDate&direction=asc"));
        Assert.Equal(2, tags.TotalCount); Assert.Equal(new[] { "Early", "Undated" }, tags.Items.Select(t => t.Title));
        foreach (var direction in new[] { "asc", "desc" })
        {
            var page = await Data<PageResponse<TrackResponse>>(await client.GetAsync($"/api/v1/tracks?sortBy=releaseDate&direction={direction}"));
            Assert.Equal(direction == "asc" ? new[] { "Early", "Late", "Undated" } : new[] { "Late", "Early", "Undated" }, page.Items.Select(t => t.Title));
        }
        using var other = factory.Client(Guid.NewGuid().ToString());
        Assert.Empty((await Data<PageResponse<TrackResponse>>(await other.GetAsync("/api/v1/tracks?tag=Late%20Night"))).Items);
    }

    [Fact]
    public async Task Csv_imports_metadata_and_rejects_bad_dates_tags_and_lengths_atomically()
    {
        using var client = factory.Client(Guid.NewGuid().ToString());
        const string header = "artist,title,durationSeconds,album,label,releaseDate,subgenre,tags\n";
        var imported = await Data<ImportResponse>(await client.PostAsJsonAsync("/api/v1/imports/csv", new CsvImportRequest
        { AuthorizationConfirmed = true, Csv = header + "A,Imported,300,Album,Label,2020-02-29,Deep House, Late  Night ;late night;Warm" }));
        var track = await Data<TrackResponse>(await client.GetAsync($"/api/v1/tracks/{imported.CreatedTrackIds.Single()}"));
        Assert.Equal("Album", track.Album); Assert.Equal("Label", track.Label); Assert.Equal("Deep House", track.Subgenre);
        Assert.Equal(new DateOnly(2020, 2, 29), track.ReleaseDate); Assert.Equal(new[] { "Late Night", "Warm" }, track.Tags);
        foreach (var row in new[]
        {
            "A,Bad,300,,,2020-02-30,,", "A,Bad,300,,,02/29/2020,,", "A,Bad,300,,,1899-12-31,,",
            "A,Bad,300,,,,,bad|tag", "A,Bad,300,,,,," + new string('t', 41),
            "A,Bad,300," + new string('a', 201) + ",,,,",
            "A,Bad,300,,,,," + string.Join(';', Enumerable.Range(1, 21).Select(i => $"tag{i}"))
        })
        {
            var response = await client.PostAsJsonAsync("/api/v1/imports/csv", new CsvImportRequest
            { AuthorizationConfirmed = true, Csv = header + "A,Would be valid,300,,,,,\n" + row });
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains("CSV record 3:", await response.Content.ReadAsStringAsync());
        }
        Assert.Equal(1, (await Data<PageResponse<TrackResponse>>(await client.GetAsync("/api/v1/tracks"))).TotalCount);
        var legacy = await Data<ImportResponse>(await client.PostAsJsonAsync("/api/v1/imports/csv", new CsvImportRequest
        { AuthorizationConfirmed = true, Csv = "artist,title,durationSeconds\nA,Legacy,300" }));
        var old = await Data<TrackResponse>(await client.GetAsync($"/api/v1/tracks/{legacy.CreatedTrackIds.Single()}"));
        Assert.Equal("", old.Album); Assert.Empty(old.Tags); Assert.Null(old.ReleaseDate);
    }
}
