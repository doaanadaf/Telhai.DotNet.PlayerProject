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
using System.Windows.Threading;
using System.Windows.Media.Imaging;
using Telhai.DotNet.PlayerProject.Models;
using Telhai.DotNet.PlayerProject.Services;

namespace Telhai.DotNet.PlayerProject
{
    public partial class MusicPlayer : Window
    {
        private readonly MediaPlayer mediaPlayer = new MediaPlayer();
        private readonly DispatcherTimer timer = new DispatcherTimer();
        private List<MusicTrack> library = new List<MusicTrack>();
        private bool isDragging = false;

        private const string FILE_NAME = "library.json";

        // API
        private readonly IItunesService _itunesService = new ItunesService();
        private CancellationTokenSource? _apiCts;
        private MusicTrack? _currentTrack;

        public MusicPlayer()
        {
            InitializeComponent();

            timer.Interval = TimeSpan.FromMilliseconds(500);
            timer.Tick += Timer_Tick;

            Loaded += MusicPlayer_Loaded;
            Closed += MusicPlayer_Closed;

            // default UI state
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
            try { _apiCts?.Cancel(); } catch { }
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

        // ---------------- UI EVENTS ----------------

        private async void BtnPlay_Click(object sender, RoutedEventArgs e)
        {
            // אם יש שיר מסומן - לנגן אותו (דרישה)
            if (lstLibrary.SelectedItem is MusicTrack track)
            {
                await PlayTrackAsync(track);
                return;
            }

            // אחרת - להמשיך לנגן משהו שכבר נטען
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

                    library.Add(track);
                }

                UpdateLibraryUI();
                SaveLibrary();
            }
        }

        private void BtnRemove_Click(object sender, RoutedEventArgs e)
        {
            if (lstLibrary.SelectedItem is MusicTrack track)
            {
                library.Remove(track);
                UpdateLibraryUI();
                SaveLibrary();
            }
        }

        // Single click – רק להציג שם + מסלול (דרישה)
        private void LstLibrary_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (lstLibrary.SelectedItem is MusicTrack track)
            {
                txtCurrentSong.Text = track.Title;
                txtStatus.Text = "Ready";
                ShowFallbackInfo(track);
            }
        }

        // Double click – לנגן + API במקביל
        private async void LstLibrary_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (lstLibrary.SelectedItem is MusicTrack track)
            {
                await PlayTrackAsync(track);
            }
        }

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

        // ---------------- CORE LOGIC ----------------

        private async Task PlayTrackAsync(MusicTrack track)
        {
            _currentTrack = track;

            // נגן מיד (לא מחכה ל-API)
            if (!File.Exists(track.FilePath))
            {
                MessageBox.Show($"File not found:\n{track.FilePath}");
                txtCurrentSong.Text = track.Title;
                txtStatus.Text = "File not found";
                ShowFallbackInfo(track);
                return;
            }

            mediaPlayer.Open(new Uri(track.FilePath));
            mediaPlayer.Play();
            timer.Start();

            txtCurrentSong.Text = track.Title;
            txtStatus.Text = "Playing";

            // להציג בסיס מיד + תמונת ברירת מחדל
            ShowFallbackInfo(track);

            // ביטול קריאה קודמת כדי למנוע קריאות מיותרות
            _apiCts?.Cancel();
            _apiCts = new CancellationTokenSource();
            var ct = _apiCts.Token;

            // חיפוש לפי שם קובץ (רווחים/מקף)
            var query = BuildQueryFromFileName(track.Title);

            try
            {
                var meta = await _itunesService.SearchAsync(query, ct);

                if (ct.IsCancellationRequested) return;
                if (_currentTrack?.FilePath != track.FilePath) return;

                if (meta == null) return; // אין תוצאות - נשאר fallback

                txtMetaTitle.Text = string.IsNullOrWhiteSpace(meta.TrackName) ? track.Title : meta.TrackName;
                txtMetaArtist.Text = string.IsNullOrWhiteSpace(meta.ArtistName) ? "-" : meta.ArtistName;
                txtMetaAlbum.Text = string.IsNullOrWhiteSpace(meta.CollectionName) ? "-" : meta.CollectionName;
                txtMetaPath.Text = track.FilePath;

                await SetCoverFromUrlOrDefaultAsync(meta.ArtworkUrl100, ct);
            }
            catch (OperationCanceledException)
            {
                // בסדר – עברנו שיר וביטלנו
            }
            catch
            {
                // דרישה: בשגיאה – להציג שם קובץ ללא סיומת ומסלול מלא
                ShowFallbackInfo(track);
            }
        }

        private string BuildQueryFromFileName(string title)
        {
            return title.Replace("-", " ").Replace("_", " ").Trim();
        }

        private void ShowFallbackInfo(MusicTrack track)
        {
            txtMetaTitle.Text = track.Title;
            txtMetaArtist.Text = "-";
            txtMetaAlbum.Text = "-";
            txtMetaPath.Text = track.FilePath;
            SetDefaultCover();
        }

        private void SetDefaultCover()
        {
            var uri = new Uri("pack://application:,,,/Resources/default_cover.png", UriKind.Absolute);
            imgCover.Source = new BitmapImage(uri);
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

        // ---------------- LIBRARY JSON ----------------

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
    }
}