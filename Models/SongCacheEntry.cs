using System.Collections.Generic;

namespace Telhai.DotNet.PlayerProject.Models
{
    public class SongCacheEntry
    {
        // מזהה ייחודי של השיר (המסלול במחשב)
        public string FilePath { get; set; }

        // שם מקומי (שם הקובץ בלי סיומת)
        public string LocalTitle { get; set; }

        // נתונים מ־iTunes API
        public string TrackName { get; set; }
        public string ArtistName { get; set; }
        public string CollectionName { get; set; }

        // תמונת עטיפה מה־API
        public string ArtworkUrl100 { get; set; }

        // שם מותאם אישית (שיתווסף בסעיף 3.2)
        public string CustomTitle { get; set; }

        // רשימת תמונות נוספות (לסעיף 3.2)
        public List<string> ExtraImages { get; set; } = new List<string>();
    }
}