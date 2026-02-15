using System.Collections.Generic;
using System.Threading.Tasks;
using Telhai.DotNet.PlayerProject.Models;

namespace Telhai.DotNet.PlayerProject.Services
{
    public interface ISongCacheService
    {
        Task<SongCacheEntry?> GetAsync(string filePath);
        Task UpsertAsync(SongCacheEntry entry);
        Task DeleteAsync(string filePath);

        // שימושי (לא חובה, אבל נוח לשירות)
        Task<List<SongCacheEntry>> GetAllAsync();
    }
}