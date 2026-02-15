using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Telhai.DotNet.PlayerProject.Models;

namespace Telhai.DotNet.PlayerProject.Services
{
    public class ItunesService : IItunesService
    {
        // מומלץ HttpClient אחד לכל האפליקציה
        private static readonly HttpClient _http = new HttpClient();

        public async Task<SongMetadata?> SearchAsync(string query, CancellationToken ct)
        {
            var url =
                $"https://itunes.apple.com/search?term={Uri.EscapeDataString(query)}&entity=song&limit=1";

            using var resp = await _http.GetAsync(url, ct);
            resp.EnsureSuccessStatusCode();

            var json = await resp.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("results", out var results) ||
                results.ValueKind != JsonValueKind.Array ||
                results.GetArrayLength() == 0)
            {
                return null;
            }

            var r0 = results[0];

            return new SongMetadata
            {
                TrackName = r0.TryGetProperty("trackName", out var t) ? t.GetString() ?? "" : "",
                ArtistName = r0.TryGetProperty("artistName", out var a) ? a.GetString() ?? "" : "",
                CollectionName = r0.TryGetProperty("collectionName", out var c) ? c.GetString() ?? "" : "",
                ArtworkUrl100 = r0.TryGetProperty("artworkUrl100", out var art) ? art.GetString() ?? "" : ""
            };
        }
    }
}