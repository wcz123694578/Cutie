using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.DependencyInjection;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Cutie.Service;
using System;
using System.Collections.ObjectModel;

namespace Cutie.ViewModels
{
    public class MainViewModel : ObservableObject
    {
        public RelayCommand OpenSettingsViewCommand { get; set; }
        private readonly SettingsService _settingsService;

        public ObservableCollection<string> ToolCallLogList { get; set; } = new ObservableCollection<string>();

        public MainViewModel()
        {
            OpenSettingsViewCommand = new RelayCommand(OnOpenSettingsView);
            _settingsService = Ioc.Default.GetRequiredService<SettingsService>();
        }

        public void AddToolCall(string message)
        {
            VegasContext.InvokeAsync<object>(() =>
            {
                ToolCallLogList.Insert(0, message);
                if (ToolCallLogList.Count > 1000) ToolCallLogList.RemoveAt(ToolCallLogList.Count - 1);
                return null;
            });
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
