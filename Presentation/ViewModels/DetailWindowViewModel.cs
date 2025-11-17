using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CSharp_WPF_Websockets.Presentation.ViewModels
{
    public partial class DetailWindowViewModel : ObservableObject
    {
        [ObservableProperty]
        private ObservableCollection<SignalCardViewModel> _selectedSignals;

        [ObservableProperty]
        private bool _hasSelectedSignals;

        public DetailWindowViewModel(IEnumerable<SignalCardViewModel> selectedSignals)
        {
            SelectedSignals = new ObservableCollection<SignalCardViewModel>(selectedSignals);
            HasSelectedSignals = SelectedSignals.Any();

            // Suscribirse a cambios en la colección
            SelectedSignals.CollectionChanged += (s, e) =>
            {
                HasSelectedSignals = SelectedSignals.Any();
            };
        }

        public void UpdateSelectedSignals(IEnumerable<SignalCardViewModel> newSelectedSignals)
        {
            SelectedSignals.Clear();
            foreach (var signal in newSelectedSignals)
            {
                SelectedSignals.Add(signal);
            }
            HasSelectedSignals = SelectedSignals.Any();
        }
    }
}