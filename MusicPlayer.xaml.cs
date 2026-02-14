using Microsoft.Win32;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace Telhai.DotNet.PlayerProject
{
    /// <summary>
    /// Interaction logic for MusicPlayer.xaml
    /// </summary>
    public partial class MusicPlayer : Window
    {
        private MediaPlayer mediaPlayer = new MediaPlayer();
        private DispatcherTimer timer = new DispatcherTimer();
        private List<MusicTrack> library = new List<MusicTrack>();
        private bool isDragging = false;
        private const string FILE_NAME = "library.json";

        public MusicPlayer()
        {
            //--init all Hardcoded xaml into Elements Tree
            InitializeComponent();

            timer.Interval = TimeSpan.FromMilliseconds(500);
            timer.Tick += new EventHandler(Timer_Tick);

            this.Loaded += MusicPlayer_Loaded;
            // this.MouseDoubleClick += MusicPlayer_MouseDoubleClick;
            // this.MouseDoubleClick += new MouseButtonEventHandler(MusicPlayer_MouseDoubleClick);
        }

        private void MusicPlayer_Loaded(object sender, RoutedEventArgs e)
        {
            this.LoadLibrary();
        }


        private void Timer_Tick(object? sender, EventArgs e)
        {
            // Update slider ONLY if music is loaded AND user is NOT holding the handle
            if (mediaPlayer.Source != null && mediaPlayer.NaturalDuration.HasTimeSpan && !isDragging)
            {
                sliderProgress.Maximum = mediaPlayer.NaturalDuration.TimeSpan.TotalSeconds;
                sliderProgress.Value = mediaPlayer.Position.TotalSeconds;
            }
        }

        // --- EMPTY PLACEHOLDERS TO MAKE IT BUILD ---
        private void BtnPlay_Click(object sender, RoutedEventArgs e)
        {
            //if (sender is Button btn)
            //{
            //    btn.Background = Brushes.LightGreen;
            //}
            

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
            isDragging = true; // Stop timer updates
        }

        private void Slider_DragCompleted(object sender, MouseButtonEventArgs e)
        {
            isDragging = false; // Resume timer updates
            mediaPlayer.Position = TimeSpan.FromSeconds(sliderProgress.Value);
        }


        private void BtnAdd_Click(object sender, RoutedEventArgs e)
        {
            //File Dialog to choose file from system
            OpenFileDialog ofd = new OpenFileDialog();
            ofd.Multiselect = true;
            ofd.Filter = "MP3 Files|*.mp3";

            //User Confirmed
            if (ofd.ShowDialog() == true)
            {
                //iterate all files selected as tring
                foreach (string file in ofd.FileNames)
                {
                    //Create Object for each filr
                    MusicTrack track = new MusicTrack
                    {
                        //Only file name
                        Title = System.IO.Path.GetFileNameWithoutExtension(file),
                        //full path
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
            //Take All library list as Source to the listbox
            //diaplay tostring for inner object whithin list
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
                //read File
                string json = File.ReadAllText(FILE_NAME);
                //Create List Of MusicTrack from json
                library = JsonSerializer.Deserialize<List<MusicTrack>>(json) ?? new List<MusicTrack>();
                //Show All loaded MusicTrack in List Box
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

        private void LstLibrary_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (lstLibrary.SelectedItem is MusicTrack track)
            {
                mediaPlayer.Open(new Uri(track.FilePath));
                mediaPlayer.Play();
                timer.Start();
                txtCurrentSong.Text = track.Title;
                txtStatus.Text = "Playing";
            }
        }

        private void BtnSettings_Click(object sender, RoutedEventArgs e)
        {
            //1) Create Settings Window Instance
            Settings settingsWin = new Settings();

            //2) Subscribe/register to OnScanCompleted Event
            settingsWin.OnScanCompleted += SettingsWin_OnScanCompleted;

            settingsWin.ShowDialog();

        }

        private void SettingsWin_OnScanCompleted(List<MusicTrack> newTracksEventData)
        {
            foreach (var track in newTracksEventData)
            {
                // Prevent duplicates based on FilePath
                if (!library.Any(x => x.FilePath == track.FilePath))
                {
                    library.Add(track);
                }
            }

            UpdateLibraryUI();
            SaveLibrary();
        }
    }



    //private void MusicPlayer_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    //{
    //    MainWindow p = new MainWindow();
    //    p.Title = "YYYYY";
    //    p.Show();
    //}
}