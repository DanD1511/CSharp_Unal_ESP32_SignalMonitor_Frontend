using System.Collections.ObjectModel;
using CSharp_WPF_Websockets.Domain.Entities;

namespace CSharp_WPF_Websockets.Infrastructure.Data
{
    public class SignalDataStore
    {
        private readonly Dictionary<string, DeviceSignal> _signals = new();
        private readonly object _lock = new();

        public ObservableCollection<DeviceSignal> Signals { get; } = new();

        public void UpdateSignal(DeviceSignal signal)
        {
            if (signal == null || string.IsNullOrWhiteSpace(signal.Id))
            {
                return;
            }

            lock (_lock)
            {
                if (_signals.ContainsKey(signal.Id))
                {
                    var existing = _signals[signal.Id];
                    existing.Value = signal.Value;
                    existing.Status = signal.Status;
                    existing.Timestamp = signal.Timestamp;
                }
                else
                {
                    _signals[signal.Id] = signal;
                    App.Current.Dispatcher.Invoke(() => Signals.Add(signal));
                }
            }
        }


        public IEnumerable<DeviceSignal> GetAllSignals()
        {
            lock (_lock)
            {
                return _signals.Values.ToList();
            }
        }

        public DeviceSignal GetSignal(string id)
        {
            lock (_lock)
            {
                return _signals.TryGetValue(id, out var signal) ? signal : null;
            }
        }

        public void Clear()
        {
            lock (_lock)
            {
                _signals.Clear();
                App.Current.Dispatcher.Invoke(() => Signals.Clear());
            }
        }
    }
}
