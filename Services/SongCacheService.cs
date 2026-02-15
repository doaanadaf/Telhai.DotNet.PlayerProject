using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Telhai.DotNet.PlayerProject.Models;

namespace Telhai.DotNet.PlayerProject.Services
{
    public class SongCacheService : ISongCacheService
    {
        private const string CACHE_FILE = "song_cache.json";

        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true
        };

        private string CachePath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, CACHE_FILE);

        public async Task<List<SongCacheEntry>> GetAllAsync()
        {
            if (!File.Exists(CachePath))
                return new List<SongCacheEntry>();

            try
            {
                var json = await File.ReadAllTextAsync(CachePath);
                return JsonSerializer.Deserialize<List<SongCacheEntry>>(json) ?? new List<SongCacheEntry>();
            }
            catch
            {
                return new List<SongCacheEntry>();
            }
        }

        public async Task<SongCacheEntry?> GetAsync(string filePath)
        {
            var all = await GetAllAsync();
            return all.FirstOrDefault(x => string.Equals(x.FilePath, filePath, StringComparison.OrdinalIgnoreCase));
        }

        public async Task UpsertAsync(SongCacheEntry entry)
        {
            var all = await GetAllAsync();

            var idx = all.FindIndex(x => string.Equals(x.FilePath, entry.FilePath, StringComparison.OrdinalIgnoreCase));
            if (idx >= 0) all[idx] = entry;
            else all.Add(entry);

            var json = JsonSerializer.Serialize(all, JsonOptions);
            await File.WriteAllTextAsync(CachePath, json);
        }

        public async Task DeleteAsync(string filePath)
        {
            var all = await GetAllAsync();

            all = all
                .Where(x => !string.Equals(x.FilePath, filePath, StringComparison.OrdinalIgnoreCase))
                .ToList();

            var json = JsonSerializer.Serialize(all, JsonOptions);
            await File.WriteAllTextAsync(CachePath, json);
        }
    }
}