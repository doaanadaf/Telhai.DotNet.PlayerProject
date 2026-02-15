using System.Threading;
using System.Threading.Tasks;
using Telhai.DotNet.PlayerProject.Models;

namespace Telhai.DotNet.PlayerProject.Services
{
    public interface IItunesService
    {
        Task<SongMetadata?> SearchAsync(string query, CancellationToken ct);
    }
}