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
        private readonly HttpClient _http = new HttpClient();

        public async Task<SongMetadata?> SearchAsync(string query, CancellationToken ct)
        {
            try
            {
                // בניית כתובת החיפוש ל-iTunes API
                var url = $"https://itunes.apple.com/search?term={Uri.EscapeDataString(query)}&entity=song&limit=1";

                // קריאה אסינכרונית ל-API
                using var response = await _http.GetAsync(url, ct);

                // בדיקה שהקריאה הצליחה
                response.EnsureSuccessStatusCode();

                // קריאת התוכן כטקסט
                var json = await response.Content.ReadAsStringAsync(ct);

                // פירוק JSON
                using var doc = JsonDocument.Parse(json);

                var results = doc.RootElement.GetProperty("results");

                // אם אין תוצאות
                if (results.GetArrayLength() == 0)
                    return null;

                var r0 = results[0];

                // יצירת אובייקט SongMetadata
                return new SongMetadata
                {
                    TrackName =
                        r0.TryGetProperty("trackName", out var track)
                        ? track.GetString() ?? ""
                        : "",

                    ArtistName =
                        r0.TryGetProperty("artistName", out var artist)
                        ? artist.GetString() ?? ""
                        : "",

                    CollectionName =
                        r0.TryGetProperty("collectionName", out var collection)
                        ? collection.GetString() ?? ""
                        : "",

                    ArtworkUrl100 =
                        r0.TryGetProperty("artworkUrl100", out var artwork)
                        ? artwork.GetString() ?? ""
                        : ""
                };
            }
            catch (OperationCanceledException)
            {
                // הקריאה בוטלה (CancellationToken)
                return null;
            }
            catch (Exception)
            {
                // שגיאה כללית
                return null;
            }
        }
    }
}