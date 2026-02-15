using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Telhai.DotNet.PlayerProject.Models;
using Telhai.DotNet.PlayerProject.Services;

namespace Telhai.DotNet.PlayerProject
{
    public partial class MusicPlayer : Window
    {
        private readonly MediaPlayer mediaPlayer = new MediaPlayer();
        private readonly DispatcherTimer timer = new DispatcherTimer();
        private readonly DispatcherTimer slideshowTimer = new DispatcherTimer();

        private List<MusicTrack> library = new List<MusicTrack>();
        private bool isDragging = false;

        private const string FILE_NAME = "library.json";

        // ===== Section 2: API =====
        private readonly IItunesService _itunesService = new ItunesService();
        private CancellationTokenSource? _apiCts;
        private MusicTrack? _currentTrack;

        // ===== Section 3.1 + 3.2: JSON Cache + Edit =====
        private readonly ISongCacheService _cacheService = new SongCacheService();

        // Slideshow state (Section 3.2 requirement)
        private List<string> _currentImages = new List<string>();
        private int _imageIndex = 0;

        public MusicPlayer()
        {
            InitializeComponent();

            timer.Interval = TimeSpan.FromMilliseconds(500);
            timer.Tick += Timer_Tick;

            slideshowTimer.Interval = TimeSpan.FromSeconds(3);
            slideshowTimer.Tick += SlideshowTimer_Tick;

            Loaded += MusicPlayer_Loaded;
            Closed += MusicPlayer_Closed;

            // default UI
            SetDefaultCover();
            txtMetaTitle.Text = "-";
            txtMetaArtist.Text = "-";
            txtMetaAlbum.Text = "-";
            txtMetaPath.Text = "-";
        }

        private void MusicPlayer_Loaded(object sender, RoutedEventArgs e)
        {
            LoadLibrary();
            mediaPlayer.Volume = sliderVolume.Value;
        }

        private void MusicPlayer_Closed(object? sender, EventArgs e)
        {
            try { slideshowTimer.Stop(); } catch { }
            try { _apiCts?.Cancel(); _apiCts?.Dispose(); } catch { }
            try { mediaPlayer.Stop(); } catch { }
        }

        private void Timer_Tick(object? sender, EventArgs e)
        {
            if (mediaPlayer.Source != null && mediaPlayer.NaturalDuration.HasTimeSpan && !isDragging)
            {
                sliderProgress.Maximum = mediaPlayer.NaturalDuration.TimeSpan.TotalSeconds;
                sliderProgress.Value = mediaPlayer.Position.TotalSeconds;
            }
        }

        // ================== Buttons ==================

        private async void BtnPlay_Click(object sender, RoutedEventArgs e)
        {
            // דרישה: PLAY מנגן את השיר המסומן
            if (lstLibrary.SelectedItem is MusicTrack track)
            {
                await ShowSongAsync(track, startPlayback: true);
                return;
            }

            // אחרת - ממשיך מה שכבר נטען
            mediaPlayer.Play();
            timer.Start();
            txtStatus.Text = "Playing";
        }

        private void BtnPause_Click(object sender, RoutedEventArgs e)
        {
            mediaPlayer.Pause();
            txtStatus.Text = "Paused";
        }

        private void BtnStop_Click(object sender, RoutedEventArgs e)
        {
            mediaPlayer.Stop();
            timer.Stop();
            sliderProgress.Value = 0;
            txtStatus.Text = "Stopped";

            slideshowTimer.Stop();
        }

        private void SliderVolume_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            mediaPlayer.Volume = sliderVolume.Value;
        }

        private void Slider_DragStarted(object sender, MouseButtonEventArgs e)
        {
            isDragging = true;
        }

        private void Slider_DragCompleted(object sender, MouseButtonEventArgs e)
        {
            isDragging = false;
            mediaPlayer.Position = TimeSpan.FromSeconds(sliderProgress.Value);
        }

        private void BtnAdd_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog ofd = new OpenFileDialog
            {
                Multiselect = true,
                Filter = "MP3 Files|*.mp3"
            };

            if (ofd.ShowDialog() == true)
            {
                foreach (string file in ofd.FileNames)
                {
                    MusicTrack track = new MusicTrack
                    {
                        Title = Path.GetFileNameWithoutExtension(file),
                        FilePath = file
                    };

                    // מניע כפילויות לפי FilePath
                    if (!library.Any(x => x.FilePath == track.FilePath))
                        library.Add(track);
                }

                UpdateLibraryUI();
                SaveLibrary();
            }
        }

        private async void BtnRemove_Click(object sender, RoutedEventArgs e)
        {
            if (lstLibrary.SelectedItem is MusicTrack track)
            {
                library.Remove(track);
                UpdateLibraryUI();
                SaveLibrary();

                // מוחק גם מה- cache JSON
                await _cacheService.DeleteAsync(track.FilePath);
            }
        }

        private void BtnEdit_Click(object sender, RoutedEventArgs e)
        {
            if (lstLibrary.SelectedItem is not MusicTrack track)
            {
                MessageBox.Show("Select a song first.");
                return;
            }

            var win = new EditSongWindow(_cacheService, track.FilePath)
            {
                Owner = this
            };

            win.ShowDialog();

            // אחרי סגירה - לרענן את ההצגה לפי JSON (בלי לנגן)
            _ = ShowSongAsync(track, startPlayback: false);
        }

        // ================== List events ==================

        // קליק רגיל: מציג מקומי + אם יש Cache מציג מה-JSON (בלי API)
        private async void LstLibrary_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (lstLibrary.SelectedItem is not MusicTrack track) return;
            await ShowSongAsync(track, startPlayback: false);
        }

        // דאבל קליק: מנגן + מציג (Cache קודם, אחרת API)
        private async void LstLibrary_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (lstLibrary.SelectedItem is not MusicTrack track) return;
            await ShowSongAsync(track, startPlayback: true);
        }

        // ================== Core flow (CACHE -> API) ==================

        private async Task ShowSongAsync(MusicTrack track, bool startPlayback)
        {
            _currentTrack = track;

            // תמיד מציגים בסיס מיד (דרישה: קליק מציג שם+מסלול)
            txtCurrentSong.Text = track.Title;
            txtStatus.Text = startPlayback ? "Playing" : "Ready";
            ShowFallbackInfo(track);

            // אם צריך לנגן – ננגן מיד (בלי לחכות ל-API/Cache)
            if (startPlayback)
            {
                if (!File.Exists(track.FilePath))
                {
                    MessageBox.Show($"File not found:\n{track.FilePath}");
                    txtStatus.Text = "File not found";
                    return;
                }

                mediaPlayer.Open(new Uri(track.FilePath));
                mediaPlayer.Play();
                timer.Start();
            }

            // ביטול קריאה קודמת (דרישה)
            _apiCts?.Cancel();
            _apiCts?.Dispose();
            _apiCts = new CancellationTokenSource();
            var ct = _apiCts.Token;

            // 1) קודם בודקים CACHE (JSON)
            SongCacheEntry? cached = null;
            try
            {
                cached = await _cacheService.GetAsync(track.FilePath);
            }
            catch
            {
                // אם יש בעיה ב-cache, פשוט ממשיכים ל-API
            }

            if (ct.IsCancellationRequested) return;
            if (_currentTrack?.FilePath != track.FilePath) return;

            if (cached != null)
            {
                ApplyCacheToUI(cached);
                await ApplyCoverAndSlideshowAsync(cached, ct, startPlayback);
                return; // ✅ לא קוראים ל-API
            }

            // 2) אין Cache -> קוראים ל-API ושומרים ל-JSON
            try
            {
                var query = BuildQueryFromFileName(track.Title);
                var meta = await _itunesService.SearchAsync(query, ct);

                if (ct.IsCancellationRequested) return;
                if (_currentTrack?.FilePath != track.FilePath) return;

                if (meta == null)
                {
                    // אין תוצאות – לפי דרישה נשאר עם local + path
                    ShowFallbackInfo(track);
                    slideshowTimer.Stop();
                    return;
                }

                // עדכון UI
                txtMetaTitle.Text = string.IsNullOrWhiteSpace(meta.TrackName) ? track.Title : meta.TrackName;
                txtMetaArtist.Text = string.IsNullOrWhiteSpace(meta.ArtistName) ? "-" : meta.ArtistName;
                txtMetaAlbum.Text = string.IsNullOrWhiteSpace(meta.CollectionName) ? "-" : meta.CollectionName;
                txtMetaPath.Text = track.FilePath;

                // שמירה ל-JSON
                var entry = new SongCacheEntry
                {
                    FilePath = track.FilePath,
                    LocalTitle = track.Title,
                    TrackName = meta.TrackName ?? "",
                    ArtistName = meta.ArtistName ?? "",
                    CollectionName = meta.CollectionName ?? "",
                    ArtworkUrl100 = meta.ArtworkUrl100 ?? "",
                    CustomTitle = "",
                    ExtraImages = new List<string>()
                };

                await _cacheService.UpsertAsync(entry);

                await ApplyCoverAndSlideshowAsync(entry, ct, startPlayback);
            }
            catch (OperationCanceledException)
            {
                // מעבר שיר – תקין
            }
            catch
            {
                // דרישה: בשגיאה – להציג שם קובץ ללא סיומת ומסלול מלא
                ShowFallbackInfo(track);
                slideshowTimer.Stop();
                SetDefaultCover();
            }
        }

        private string BuildQueryFromFileName(string title)
        {
            // דרישה: שם אומן/שיר מופרדים ברווחים או מקף -> ננקה קצת
            return title.Replace("-", " ").Replace("_", " ").Trim();
        }

        private void ApplyCacheToUI(SongCacheEntry entry)
        {
            var titleToShow =
                !string.IsNullOrWhiteSpace(entry.CustomTitle) ? entry.CustomTitle :
                !string.IsNullOrWhiteSpace(entry.TrackName) ? entry.TrackName :
                entry.LocalTitle;

            txtMetaTitle.Text = titleToShow;
            txtMetaArtist.Text = string.IsNullOrWhiteSpace(entry.ArtistName) ? "-" : entry.ArtistName;
            txtMetaAlbum.Text = string.IsNullOrWhiteSpace(entry.CollectionName) ? "-" : entry.CollectionName;
            txtMetaPath.Text = entry.FilePath;
        }

        private void ShowFallbackInfo(MusicTrack track)
        {
            txtMetaTitle.Text = track.Title;
            txtMetaArtist.Text = "-";
            txtMetaAlbum.Text = "-";
            txtMetaPath.Text = track.FilePath;

            slideshowTimer.Stop();
            _currentImages = new List<string>();
            _imageIndex = 0;

            SetDefaultCover();
        }

        // ================== Cover + slideshow (Section 3.2) ==================

        private async Task ApplyCoverAndSlideshowAsync(SongCacheEntry entry, CancellationToken ct, bool startPlayback)
        {
            // אם יש תמונות שהוסיפו בחלון העריכה -> נציג אותן בלופ
            var extra = (entry.ExtraImages ?? new List<string>())
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .ToList();

            _currentImages = extra;
            _imageIndex = 0;

            if (_currentImages.Count > 0)
            {
                // הצג תמונה ראשונה מיד
                SetCoverFromLocalOrDefault(_currentImages[0]);

                // בלופ רק אם אנחנו “בזמן ניגון”
                // (אם את רוצה שגם בקליק רגיל ירוץ בלופ - תגידי)
                if (startPlayback)
                    slideshowTimer.Start();
                else
                    slideshowTimer.Stop();

                return;
            }

            // אין תמונות עריכה -> מציגים תמונת API כרגיל (ואין לופ)
            slideshowTimer.Stop();

            if (!string.IsNullOrWhiteSpace(entry.ArtworkUrl100))
                await SetCoverFromUrlOrDefaultAsync(entry.ArtworkUrl100, ct);
            else
                SetDefaultCover();
        }

        private void SlideshowTimer_Tick(object? sender, EventArgs e)
        {
            if (_currentTrack == null) { slideshowTimer.Stop(); return; }
            if (_currentImages.Count == 0) { slideshowTimer.Stop(); return; }

            _imageIndex++;
            if (_imageIndex >= _currentImages.Count) _imageIndex = 0;

            SetCoverFromLocalOrDefault(_currentImages[_imageIndex]);
        }

        private void SetCoverFromLocalOrDefault(string? path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                SetDefaultCover();
                return;
            }

            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.UriSource = new Uri(path, UriKind.Absolute);
                bmp.EndInit();
                bmp.Freeze();
                imgCover.Source = bmp;
            }
            catch
            {
                SetDefaultCover();
            }
        }

        private void SetDefaultCover()
        {
            try
            {
                var uri = new Uri("pack://application:,,,/Resources/default_cover.png", UriKind.Absolute);
                imgCover.Source = new BitmapImage(uri);
            }
            catch
            {
                imgCover.Source = null;
            }
        }

        private async Task SetCoverFromUrlOrDefaultAsync(string? url, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                SetDefaultCover();
                return;
            }

            try
            {
                using var http = new HttpClient();
                var bytes = await http.GetByteArrayAsync(url, ct);
                if (ct.IsCancellationRequested) return;

                using var ms = new MemoryStream(bytes);
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.StreamSource = ms;
                bmp.EndInit();
                bmp.Freeze();

                imgCover.Source = bmp;
            }
            catch
            {
                SetDefaultCover();
            }
        }

        // ================== Library JSON ==================

        private void UpdateLibraryUI()
        {
            lstLibrary.ItemsSource = null;
            lstLibrary.ItemsSource = library;
        }

        private void SaveLibrary()
        {
            var options = new JsonSerializerOptions { WriteIndented = true };
            string json = JsonSerializer.Serialize(library, options);
            File.WriteAllText(FILE_NAME, json);
        }

        private void LoadLibrary()
        {
            if (File.Exists(FILE_NAME))
            {
                string json = File.ReadAllText(FILE_NAME);
                library = JsonSerializer.Deserialize<List<MusicTrack>>(json) ?? new List<MusicTrack>();
                UpdateLibraryUI();
            }
        }

        // ================== Settings (אם יש אצלך) ==================
        private void BtnSettings_Click(object sender, RoutedEventArgs e)
        {
            Settings settingsWin = new Settings();
            settingsWin.OnScanCompleted += SettingsWin_OnScanCompleted;
            settingsWin.ShowDialog();
        }

        private void SettingsWin_OnScanCompleted(List<MusicTrack> newTracksEventData)
        {
            foreach (var track in newTracksEventData)
            {
                if (!library.Any(x => x.FilePath == track.FilePath))
                    library.Add(track);
            }

            UpdateLibraryUI();
            SaveLibrary();
        }
    }
}