using Microsoft.Win32;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Telhai.DotNet.PlayerProject.Models;
using Telhai.DotNet.PlayerProject.Services;

namespace Telhai.DotNet.PlayerProject.ViewModels
{
    public class EditSongViewModel : ObservableObject
    {
        private readonly ISongCacheService _cacheService;
        private readonly string _filePath;

        // Entry של השיר מתוך ה-JSON
        public SongCacheEntry Entry { get; private set; } = null!;

        // FilePath לשימוש ב-UI
        public string FilePath => _filePath;

        // Custom title editable
        private string _customTitle = "";
        public string CustomTitle
        {
            get => _customTitle;
            set => SetProperty(ref _customTitle, value);
        }

        // רשימת תמונות
        public ObservableCollection<string> Images { get; } =
            new ObservableCollection<string>();

        // תמונה מסומנת
        private string? _selectedImage;
        public string? SelectedImage
        {
            get => _selectedImage;
            set
            {
                if (SetProperty(ref _selectedImage, value))
                    ((RelayCommand)RemoveImageCommand)
                        .RaiseCanExecuteChanged();
            }
        }

        // Commands
        public ICommand AddImageCommand { get; }
        public ICommand RemoveImageCommand { get; }
        public ICommand SaveCommand { get; }

        // Constructor
        public EditSongViewModel(
            ISongCacheService cacheService,
            string filePath)
        {
            _cacheService = cacheService;
            _filePath = filePath;

            AddImageCommand =
                new RelayCommand(AddImage);

            RemoveImageCommand =
                new RelayCommand(
                    RemoveSelectedImage,
                    () => SelectedImage != null);

            SaveCommand =
                new RelayCommand(async () => await SaveAsync());
        }

        // Load data from JSON cache
        public async Task LoadAsync()
        {
            try
            {
                var loaded =
                    await _cacheService.GetAsync(_filePath);

                Entry = loaded ?? new SongCacheEntry
                {
                    FilePath = _filePath,
                    LocalTitle =
                        Path.GetFileNameWithoutExtension(_filePath),
                    TrackName = "",
                    ArtistName = "",
                    CollectionName = "",
                    ArtworkUrl100 = "",
                    CustomTitle = "",
                    ExtraImages =
                        new System.Collections.Generic.List<string>()
                };

                CustomTitle = Entry.CustomTitle ?? "";

                Images.Clear();

                if (Entry.ExtraImages != null)
                {
                    foreach (var img in Entry.ExtraImages
                        .Where(x => !string.IsNullOrWhiteSpace(x)))
                    {
                        Images.Add(img);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Load failed: " + ex.Message);
            }
        }

        // Add images
        private void AddImage()
        {
            var dialog = new OpenFileDialog
            {
                Filter =
                    "Images|*.png;*.jpg;*.jpeg;*.bmp",
                Multiselect = true
            };

            if (dialog.ShowDialog() == true)
            {
                foreach (var file in dialog.FileNames)
                {
                    if (!Images.Contains(file))
                        Images.Add(file);
                }
            }
        }

        // Remove image
        private void RemoveSelectedImage()
        {
            if (SelectedImage == null)
                return;

            Images.Remove(SelectedImage);

            SelectedImage = null;
        }

        // Save to JSON
        public async Task SaveAsync()
        {
            if (Entry == null)
            {
                MessageBox.Show("Nothing to save.");
                return;
            }

            Entry.CustomTitle =
                CustomTitle?.Trim() ?? "";

            Entry.ExtraImages =
                Images.ToList();

            try
            {
                await _cacheService.UpsertAsync(Entry);

                MessageBox.Show("Saved successfully ✅");
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Save failed: " + ex.Message);
            }
        }
    }
}