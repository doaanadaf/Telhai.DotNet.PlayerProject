using System.Windows;
using Telhai.DotNet.PlayerProject.Services;
using Telhai.DotNet.PlayerProject.ViewModels;

namespace Telhai.DotNet.PlayerProject
{
    public partial class EditSongWindow : Window
    {
        private readonly EditSongViewModel _vm;

        public EditSongWindow(ISongCacheService cacheService, string filePath)
        {
            InitializeComponent();

            _vm = new EditSongViewModel(cacheService, filePath);
            DataContext = _vm;

            Loaded += EditSongWindow_Loaded;
        }

        private async void EditSongWindow_Loaded(object sender, RoutedEventArgs e)
        {
            await _vm.LoadAsync();
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}