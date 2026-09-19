using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;

namespace Cutie.ViewModels
{
    public class MainViewModel : ObservableObject
    {
        public RelayCommand OpenSettingsViewCommand { get; set; }

        public MainViewModel()
        {
            OpenSettingsViewCommand = new RelayCommand(OnOpenSettingsView);
        }

        private void OnOpenSettingsView()
        {
            WeakReferenceMessenger.Default.Send(new OpenSettingsViewMessage());
        }
    }

    public class OpenSettingsViewMessage { };
}
