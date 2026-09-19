using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.DependencyInjection;
using CommunityToolkit.Mvvm.Input;
using Cutie.Models;
using Cutie.Service;
using System;

namespace Cutie.ViewModels
{
    public class SettingsViewModel : ObservableObject
    {
        private readonly SettingsService _configureService;

        public SettingsViewModel()
        {
            _configureService = Ioc.Default.GetRequiredService<SettingsService>();

            _settingsData = _configureService.GetSettings();

            this.SaveSettingsCommand = new RelayCommand(OnSaveSettings);
        }

        private void OnSaveSettings()
        {
            _configureService.SaveSettings(_settingsData);
        }

        private SettingsModel _settingsData;

        public int McpServerPort
        {
            get => _settingsData.McpServerPort;
            set
            {
                if (_settingsData.McpServerPort != value)
                {
                    _settingsData.McpServerPort = value;
                    OnPropertyChanged();
                }
            }
        }

        public RelayCommand SaveSettingsCommand { get; set; }
    }
}
