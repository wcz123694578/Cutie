using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.DependencyInjection;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Cutie.Service;

namespace Cutie.ViewModels
{
    public class MainViewModel : ObservableObject
    {
        public RelayCommand OpenSettingsViewCommand { get; set; }
        private readonly SettingsService _settingsService;

        public MainViewModel()
        {
            OpenSettingsViewCommand = new RelayCommand(OnOpenSettingsView);
            _settingsService = Ioc.Default.GetRequiredService<SettingsService>();
        }

        private void OnOpenSettingsView()
        {
            WeakReferenceMessenger.Default.Send(new OpenSettingsViewMessage());
        }

        public string CurrentListeningAddress
        {
            get
            {
                return $"http://127.0.0.1:{_settingsService.GetSettings().McpServerPort}";
            }
        }
    }

    public class OpenSettingsViewMessage { };
}
