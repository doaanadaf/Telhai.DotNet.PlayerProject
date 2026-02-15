using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Telhai.DotNet.PlayerProject.Services;

namespace Telhai.DotNet.PlayerProject
{
    public partial class MusicPlayer : Window
    {
        private MediaPlayer mediaPlayer = new MediaPlayer();
        private DispatcherTimer timer = new DispatcherTimer();
        private List<MusicTrack> library = new List<MusicTrack>();
        private bool isDragging = false;
        private const string FILE_NAME = "library.json";

        // iTunes + Cancellation
        private readonly IItunesService _itunesService = new ItunesService();
        private CancellationTokenSource? _cts;
        private readonly System.Net.Http.HttpClient _http = new System.Net.Http.HttpClient();

        private MusicTrack? _currentTrack = null;

        public MusicPlayer()
        {
            InitializeComponent();

            // ==== DEBUG AUDIO EVENTS (to understand why it doesn't play) ====
            mediaPlayer.MediaFailed += (s, e) =>
            {
                MessageBox.Show("MediaFailed: " + (e.ErrorException?.Message ?? "Unknown error"));
            };

            mediaPlayer.MediaOpened += (s, e) =>
            {
                // Optional: you can show this to verify open succeeded
                // MessageBox.Show("MediaOpened");
            };
            // ===============================================================

            timer.Interval = TimeSpan.FromMilliseconds(500);
            timer.Tick += new EventHandler(Timer_Tick);

            this.Loaded += MusicPlayer_Loaded;

            // Default UI
            SetDefaultCover();
            txtMetaTitle.Text = "-";
            txtMetaArtist.Text = "-";
            txtMetaAlbum.Text = "-";
            txtLocalPath.Text = "-";
            txtApiError.Text = "";
        }

        private void MusicPlayer_Loaded(object sender, RoutedEventArgs e)
        {
            this.LoadLibrary();
        }

        private void Timer_Tick(object? sender, EventArgs e)
        {
            if (mediaPlayer.Source != null && mediaPlayer.NaturalDuration.HasTimeSpan && !isDragging)
            {
                sliderProgress.Maximum = mediaPlayer.NaturalDuration.TimeSpan.TotalSeconds;
                sliderProgress.Value = mediaPlayer.Position.TotalSeconds;
            }
        }

        // PLAY: אם יש שיר נבחר -> נגן אותו
        private async void BtnPlay_Click(object sender, RoutedEventArgs e)
        {
            if (lstLibrary.SelectedItem is MusicTrack track)
            {
                if (_currentTrack == null || _currentTrack.FilePath != track.FilePath || mediaPlayer.Source == null)
                {
                    await StartSongAndFetchMetadataAsync(track);
                    return;
                }
            }

            try
            {
                mediaPlayer.Volume = sliderVolume.Value;
                mediaPlayer.Play();
                timer.Start();
                txtStatus.Text = "Playing";
            }
            catch (Exception ex)
            {
                MessageBox.Show("Play error: " + ex.Message);
            }
        }

        private void BtnPause_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                mediaPlayer.Pause();
                txtStatus.Text = "Paused";
            }
            catch (Exception ex)
            {
                MessageBox.Show("Pause error: " + ex.Message);
            }
        }

        private void BtnStop_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                mediaPlayer.Stop();
                timer.Stop();
                sliderProgress.Value = 0;
                txtStatus.Text = "Stopped";
            }
            catch (Exception ex)
            {
                MessageBox.Show("Stop error: " + ex.Message);
            }
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
            try
            {
                mediaPlayer.Position = TimeSpan.FromSeconds(sliderProgress.Value);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Seek error: " + ex.Message);
            }
        }

        private void BtnAdd_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog ofd = new OpenFileDialog();
            ofd.Multiselect = true;
            ofd.Filter = "MP3 Files|*.mp3";

            if (ofd.ShowDialog() == true)
            {
                foreach (string file in ofd.FileNames)
                {
                    MusicTrack track = new MusicTrack
                    {
                        Title = System.IO.Path.GetFileNameWithoutExtension(file),
                        FilePath = file
                    };
                    library.Add(track);
                }
                UpdateLibraryUI();
                SaveLibrary();
            }
        }

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

        private void BtnRemove_Click(object sender, RoutedEventArgs e)
        {
            if (lstLibrary.SelectedItem is MusicTrack track)
            {
                library.Remove(track);
                UpdateLibraryUI();
                SaveLibrary();
            }
        }

        // לחיצה אחת: רק מציגים שם + מסלול (בלי API)
        private void LstLibrary_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (lstLibrary.SelectedItem is MusicTrack track)
            {
                txtCurrentSong.Text = track.Title;
                txtLocalPath.Text = track.FilePath;
            }
        }

        // לחיצה כפולה: ניגון + API במקביל
        private async void LstLibrary_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (lstLibrary.SelectedItem is MusicTrack track)
            {
                await StartSongAndFetchMetadataAsync(track);
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
                {
                    library.Add(track);
                }
            }

            UpdateLibraryUI();
            SaveLibrary();
        }

        // -----------------------------
        // iTunes metadata + cover logic
        // -----------------------------

        private static string BuildSearchQueryFromFileName(string title)
        {
            return title.Replace("-", " ").Replace("_", " ").Trim();
        }

        private void SetDefaultCover()
        {
            try
            {
                imgCover.Source = new BitmapImage(new Uri("pack://application:,,,/Resources/default_cover.png"));
            }
            catch
            {
                imgCover.Source = null;
            }
        }

        private async Task LoadCoverAsync(string url, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                SetDefaultCover();
                return;
            }

            var bytes = await _http.GetByteArrayAsync(url, ct);
            ct.ThrowIfCancellationRequested();

            using var ms = new MemoryStream(bytes);

            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.StreamSource = ms;
            bmp.EndInit();
            bmp.Freeze();

            imgCover.Source = bmp;
        }

        private async Task StartSongAndFetchMetadataAsync(MusicTrack track)
        {
            // 1) ניגון מיידי
            _currentTrack = track;

            try
            {
                // Important: stop previous playback before opening a new one
                mediaPlayer.Stop();

                mediaPlayer.Volume = sliderVolume.Value;

                // Ensure file exists
                if (!File.Exists(track.FilePath))
                {
                    MessageBox.Show("File not found:\n" + track.FilePath);
                    return;
                }

                mediaPlayer.Open(new Uri(track.FilePath, UriKind.Absolute));
                mediaPlayer.Play();
                timer.Start();

                txtStatus.Text = "Playing";
                txtCurrentSong.Text = track.Title;
                txtLocalPath.Text = track.FilePath;
            }
            catch (Exception ex)
            {
                MessageBox.Show("Playback error: " + ex.Message);
                txtStatus.Text = "Playback error";
            }

            // Reset UI meta
            txtApiError.Text = "";
            txtMetaTitle.Text = track.Title;
            txtMetaArtist.Text = "-";
            txtMetaAlbum.Text = "-";
            SetDefaultCover();

            // 2) ביטול קריאה קודמת
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = new CancellationTokenSource();
            var ct = _cts.Token;

            var query = BuildSearchQueryFromFileName(track.Title);

            try
            {
                // 3) קריאת API במקביל לניגון
                var meta = await _itunesService.SearchAsync(query, ct);
                if (ct.IsCancellationRequested) return;

                if (meta == null)
                {
                    txtMetaTitle.Text = track.Title;
                    txtMetaArtist.Text = "-";
                    txtMetaAlbum.Text = "-";
                    SetDefaultCover();
                    return;
                }

                txtMetaTitle.Text = string.IsNullOrWhiteSpace(meta.TrackName) ? track.Title : meta.TrackName;
                txtMetaArtist.Text = string.IsNullOrWhiteSpace(meta.ArtistName) ? "-" : meta.ArtistName;
                txtMetaAlbum.Text = string.IsNullOrWhiteSpace(meta.CollectionName) ? "-" : meta.CollectionName;

                await LoadCoverAsync(meta.ArtworkUrl100, ct);
            }
            catch (OperationCanceledException)
            {
                // תקין
            }
            catch
            {
                txtApiError.Text = $"API error. Local file:\n{track.Title}\n{track.FilePath}";
                txtMetaTitle.Text = track.Title;
                txtMetaArtist.Text = "-";
                txtMetaAlbum.Text = "-";
                SetDefaultCover();
            }
        }
    }
}