using System;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CSharp_WPF_Websockets.Domain.Entities;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;

namespace CSharp_WPF_Websockets.Presentation.ViewModels
{
    public partial class SignalCardViewModel : ObservableObject, IDisposable
    {
        // ---------- Original properties & fields ----------
        private DeviceSignal _deviceSignal;

        private readonly DispatcherTimer _uiUpdateTimer;
        private readonly DispatcherTimer _cleanupTimer;
        private bool _pendingUIUpdate = false;
        private bool _pendingChartUpdate = false;
        private readonly object _uiUpdateLock = new object();

        private DateTime _lastChartUpdate = DateTime.MinValue;
        // Refrescamos ejes más rápido para señales rápidas
        private readonly TimeSpan _chartUpdateThreshold = TimeSpan.FromMilliseconds(30);

        public ObservableCollection<ISeries> ChartSeries { get; } = new();
        public ObservableCollection<DateTimePoint> ChartValues { get; } = new();

        [ObservableProperty] private Axis[] _xAxes;
        [ObservableProperty] private Axis[] _yAxes;
        [ObservableProperty] private bool _isSelected = false;
        [ObservableProperty] private LineSeries<DateTimePoint> _lineSeriesChart;

        [ObservableProperty] private bool _isAutoScale = true;
        [ObservableProperty] private double _yMin = 0;
        [ObservableProperty] private double _yMax = 3.3;
        [ObservableProperty] private double _timeWindowMinutes = 2;
        [ObservableProperty] private bool _showScaleControls = false;

        [ObservableProperty] private double _manualYMin = 0;
        [ObservableProperty] private double _manualYMax = 3.3;
        [ObservableProperty] private double _manualTimeWindow = 2;

        public object Sync { get; } = new object();

        // ---------- DSP pipeline fields ----------
        private readonly ConcurrentQueue<RawSample> _rawQueue = new();
        private readonly ConcurrentQueue<DateTimePoint> _resampledQueue = new();
        private readonly CancellationTokenSource _dspCts = new();
        private Task _dspTask;

        // --- CORRECCIÓN CRÍTICA PARA 100Hz ---
        // Necesitamos al menos 2.5x la frecuencia máxima. 
        // 100Hz * 5 = 500Hz nos dará una señal muy fiel.
        private readonly double _targetSampleRate = 500.0;

        private readonly double _targetSamplePeriodMs;
        private readonly double _sincCutoffHz;
        private readonly double _kernelHalfWidthSec;
        private readonly int _kernelHalfWidthSamplesApprox;
        private DateTime _lastResampleTime = DateTime.MinValue;

        private long _totalRawReceived = 0;
        private long _totalResampledProduced = 0;

        public SignalCardViewModel(DeviceSignal deviceSignal)
        {
            _deviceSignal = deviceSignal ?? throw new ArgumentNullException(nameof(deviceSignal));

            _targetSamplePeriodMs = 1000.0 / _targetSampleRate;

            // Nyquist en 250Hz. Esto permite pasar señales de 100Hz sin atenuación.
            _sincCutoffHz = 0.5 * _targetSampleRate;

            // Ajustamos la ventana del kernel para ser más rápida (menos latencia)
            _kernelHalfWidthSec = 0.02; // 20ms de ventana
            _kernelHalfWidthSamplesApprox = (int)Math.Ceiling(_kernelHalfWidthSec * 1000.0 / 1.0);

            _uiUpdateTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(100) // UI update un poco más rápido
            };
            _uiUpdateTimer.Tick += OnUIUpdateTimer;

            _cleanupTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromSeconds(2)
            };
            _cleanupTimer.Tick += OnCleanupTimer;
            _cleanupTimer.Start();

            InitializeChart();
            InitializeScaleDefaults();

            _dspTask = Task.Run(() => DspWorkerLoopAsync(_dspCts.Token));

            Debug.WriteLine($"[{_deviceSignal.Name}] DSP pipeline started - targetRate={_targetSampleRate}Hz");
        }

        public string Name => _deviceSignal.Name;
        public double Value => _deviceSignal.Value;
        public string Unit => _deviceSignal.Unit;
        public DateTime Timestamp => _deviceSignal.Timestamp;
        public string Color => _deviceSignal.Color;
        public double MinValue => _deviceSignal.MinValue;
        public double MaxValue => _deviceSignal.MaxValue;
        public DeviceSignal Signal => _deviceSignal;

        public string DisplayValue =>
            (!string.IsNullOrEmpty(_deviceSignal.Unit) && _deviceSignal.Unit.ToLower() == "bool")
                    ? (_deviceSignal.Value > 0 ? "ON" : "OFF")
                    : $"{_deviceSignal.Value:F1} {_deviceSignal.Unit}";

        public string StatusText
        {
            get
            {
                var diff = DateTime.Now - _deviceSignal.Timestamp;
                if (diff.TotalSeconds < 5) return "Live";
                if (diff.TotalMinutes < 1) return "Recent";
                return "Stale";
            }
        }

        public Brush StatusColor
        {
            get
            {
                var diff = DateTime.Now - _deviceSignal.Timestamp;
                if (diff.TotalSeconds < 5) return Brushes.Green;
                if (diff.TotalMinutes < 1) return Brushes.Orange;
                return Brushes.Red;
            }
        }

        private void InitializeChart()
        {
            LineSeriesChart = new LineSeries<DateTimePoint>
            {
                Values = ChartValues,
                Name = _deviceSignal.Name ?? "Signal",
                Fill = null,
                GeometryStroke = null,
                GeometryFill = null,
                AnimationsSpeed = TimeSpan.Zero,
                EnableNullSplitting = false,
                // Optimizamos el renderizado para muchos puntos
                Stroke = new SolidColorPaint(SKColor.Parse(_deviceSignal.Color ?? "#1ABC9C")) { StrokeThickness = 1 }
            };

            ChartSeries.Clear();
            ChartSeries.Add(LineSeriesChart);
        }

        private void InitializeScaleDefaults()
        {
            _manualYMin = _yMin;
            _manualYMax = _yMax;
            _manualTimeWindow = _timeWindowMinutes;
            CreateAxes();
        }

        private TimeSpan GetTimeStep() => TimeSpan.FromMilliseconds(50);
        private Func<DateTime, string> GetTimeFormatter() => date => date.ToString("HH:mm:ss.fff");

        private void CreateAxes()
        {
            var now = DateTime.Now;
            var timeWindow = TimeSpan.FromMinutes(_timeWindowMinutes);

            XAxes = new Axis[]
            {
                new DateTimeAxis(GetTimeStep(), GetTimeFormatter())
                {
                    Name = "Tiempo",
                    LabelsPaint = new SolidColorPaint(SKColors.LightGray),
                    TextSize = 10,
                    MinLimit = (now - timeWindow).Ticks,
                    MaxLimit = now.Ticks,
                    ForceStepToMin = false,
                    IsVisible = true
                }
            };

            YAxes = new Axis[]
            {
                new Axis
                {
                    LabelsPaint = new SolidColorPaint(SKColors.LightGray),
                    TextSize = 10,
                    MinLimit = _yMin,
                    MaxLimit = _yMax,
                    ForceStepToMin = false,
                    IsVisible = true
                }
            };
        }

        public void UpdateSignal(DeviceSignal newSignal)
        {
            if (newSignal == null) return;
            _deviceSignal = newSignal;

            // Como arreglamos el repositorio, newSignal.Timestamp ya es el DateTime correcto
            // pero internamente trabajamos con UTC para el DSP
            DateTime baseTsUtc = newSignal.Timestamp.ToUniversalTime();

            if (newSignal.Samples != null)
            {
                foreach (var sp in newSignal.Samples)
                {
                    DateTime sampleTime = baseTsUtc.AddSeconds(sp.T);

                    var raw = new RawSample
                    {
                        TimestampUtc = sampleTime,
                        Value = sp.Value
                    };

                    _rawQueue.Enqueue(raw);
                    Interlocked.Increment(ref _totalRawReceived);
                }
            }

            lock (_uiUpdateLock)
            {
                _pendingUIUpdate = true;
                if (!_uiUpdateTimer.IsEnabled)
                    _uiUpdateTimer.Start();
            }
        }

        private async Task DspWorkerLoopAsync(CancellationToken ct)
        {
            var smallLatency = TimeSpan.FromMilliseconds(20);
            _lastResampleTime = DateTime.UtcNow;

            var rawList = new System.Collections.Generic.List<RawSample>();

            try
            {
                while (!ct.IsCancellationRequested)
                {
                    rawList.Clear();
                    while (_rawQueue.TryDequeue(out var s))
                    {
                        rawList.Add(s);
                    }

                    if (rawList.Count == 0)
                    {
                        await Task.Delay(2, ct).ConfigureAwait(false); // Menor delay para 500Hz
                        continue;
                    }

                    AppendToHistory(rawList);

                    var resampleUpTo = DateTime.UtcNow - smallLatency;

                    if (_lastResampleTime == DateTime.MinValue)
                        _lastResampleTime = resampleUpTo - TimeSpan.FromMilliseconds(_targetSamplePeriodMs);

                    var nextTime = _lastResampleTime + TimeSpan.FromMilliseconds(_targetSamplePeriodMs);
                    var produced = 0;

                    while (nextTime <= resampleUpTo)
                    {
                        var value = SincInterpolate(nextTime);
                        var point = new DateTimePoint(nextTime.ToLocalTime(), value);
                        _resampledQueue.Enqueue(point);
                        Interlocked.Increment(ref _totalResampledProduced);
                        produced++;

                        _lastResampleTime = nextTime;
                        nextTime = _lastResampleTime + TimeSpan.FromMilliseconds(_targetSamplePeriodMs);
                    }

                    if (produced == 0) await Task.Delay(1, ct).ConfigureAwait(false);
                    else await Task.Delay(1, ct).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Debug.WriteLine($"[DSP] Unexpected error: {ex}");
            }
        }

        private readonly System.Collections.Generic.List<RawSample> _history = new();
        private readonly object _historyLock = new();

        private void AppendToHistory(System.Collections.Generic.List<RawSample> incoming)
        {
            lock (_historyLock)
            {
                _history.AddRange(incoming);
                _history.Sort((a, b) => a.TimestampUtc.CompareTo(b.TimestampUtc));

                var cutoff = DateTime.UtcNow - TimeSpan.FromSeconds(5); // 5 segundos es suficiente

                int removeCount = 0;
                if (_history.Count > 0 && _history[0].TimestampUtc < cutoff)
                {
                    removeCount = _history.TakeWhile(s => s.TimestampUtc < cutoff).Count();
                }

                if (removeCount > 0)
                {
                    _history.RemoveRange(0, removeCount);
                }
            }
        }

        private double SincInterpolate(DateTime targetUtc)
        {
            var t = targetUtc.ToUniversalTime();
            double tSec = (t - DateTime.UnixEpoch).TotalSeconds;

            RawSample[] hist;
            lock (_historyLock)
            {
                hist = _history.ToArray();
            }

            if (hist.Length == 0)
                return _deviceSignal.Value;

            double fc = _sincCutoffHz;
            double a = _kernelHalfWidthSec;
            double sum = 0.0;
            double wsum = 0.0;

            // Optimizamos búsqueda del rango relevante
            for (int i = 0; i < hist.Length; i++)
            {
                var si = hist[i];
                double siSec = (si.TimestampUtc - DateTime.UnixEpoch).TotalSeconds;
                double dt = tSec - siSec;

                if (Math.Abs(dt) > a) continue;

                double x = 2.0 * Math.PI * fc * dt;
                double sinc = SafeSinc(x);
                double u = dt / a;
                double lanczos = SafeLanczos(u);

                double w = sinc * lanczos;
                sum += si.Value * w;
                wsum += Math.Abs(w);
            }

            if (wsum <= 1e-12)
            {
                // Fallback rápido
                if (hist.Length > 0) return hist[hist.Length - 1].Value;
                return _deviceSignal.Value;
            }

            return sum / wsum;
        }

        private static double SafeSinc(double x)
        {
            if (Math.Abs(x) < 1e-8) return 1.0;
            return Math.Sin(x) / x;
        }

        private static double SafeLanczos(double u)
        {
            if (Math.Abs(u) > 1.0) return 0.0;
            if (Math.Abs(u) < 1e-8) return 1.0;
            double piU = Math.PI * u;
            return Math.Sin(piU) / piU;
        }

        private void OnUIUpdateTimer(object sender, EventArgs e)
        {
            var any = false;
            lock (Sync)
            {
                while (_resampledQueue.TryDequeue(out var p))
                {
                    ChartValues.Add(p);
                    any = true;
                    _pendingChartUpdate = true;
                }

                var now = DateTime.Now;
                var window = TimeSpan.FromMinutes(TimeWindowMinutes);
                var cutoff = now - window - TimeSpan.FromSeconds(2);

                // Limpieza rápida
                while (ChartValues.Count > 0 && ChartValues[0].DateTime < cutoff)
                    ChartValues.RemoveAt(0);
            }

            if (any)
            {
                var now = DateTime.Now;
                if (now - _lastChartUpdate > _chartUpdateThreshold)
                {
                    _lastChartUpdate = now;
                    if (System.Windows.Application.Current?.Dispatcher?.CheckAccess() == true)
                        UpdateAxesThrottled();
                    else
                        System.Windows.Application.Current?.Dispatcher?.BeginInvoke(UpdateAxesThrottled, DispatcherPriority.Background);
                }
                SchedulePropertyNotifications();
            }

            lock (_uiUpdateLock)
            {
                _pendingUIUpdate = false;
                _uiUpdateTimer.Stop();
            }
        }

        private void SchedulePropertyNotifications()
        {
            lock (_uiUpdateLock)
            {
                _pendingUIUpdate = true;
                if (!_uiUpdateTimer.IsEnabled)
                    _uiUpdateTimer.Start();
            }
        }

        private void UpdateAxesThrottled()
        {
            if (!_pendingChartUpdate) return;
            lock (Sync)
            {
                _pendingChartUpdate = false;
                UpdateXAxisLimits();
                if (IsAutoScale) UpdateYAxisLimits();
                else ApplyYAxisLimits(YMin, YMax);
            }
        }

        private void UpdateXAxisLimits()
        {
            if (XAxes?.Length > 0)
            {
                var now = DateTime.Now;
                var window = TimeSpan.FromMinutes(TimeWindowMinutes);
                XAxes[0].MinLimit = (now - window).Ticks;
                XAxes[0].MaxLimit = now.Ticks;
            }
        }

        private void UpdateYAxisLimits()
        {
            if (ChartValues.Count < 5 || YAxes?.Length == 0) return;

            var now = DateTime.Now;
            var window = TimeSpan.FromMinutes(TimeWindowMinutes);
            var cutoffTime = now - window;

            // Tomamos una muestra representativa para no recorrer todo
            var visibleValues = ChartValues
                .Where(p => p.DateTime >= cutoffTime)
                .Select(p => p.Value); // IEnumerable, no array para velocidad

            if (!visibleValues.Any()) return;

            var min = visibleValues.Min();
            var max = visibleValues.Max();
            var range = max - min;

            if (range < 0.01)
            {
                range = Math.Max(0.5, Math.Abs((double)min) * 0.2);
                min -= range / 2;
                max += range / 2;
            }

            var padding = range * 0.15;
            var newYMin = min - padding;
            var newYMax = max + padding;

            if (_deviceSignal.MinValue < _deviceSignal.MaxValue)
            {
                newYMin = Math.Max((double)newYMin, _deviceSignal.MinValue);
                newYMax = Math.Min((double)newYMax, _deviceSignal.MaxValue);
            }

            if (Math.Abs(YMin - (double)newYMin) > range * 0.05 || Math.Abs(YMax - (double)newYMax) > range * 0.05)
            {
                YMin = (double)newYMin;
                YMax = (double)newYMax;
                ApplyYAxisLimits((double)newYMin, (double)newYMax);
            }
        }

        private void ApplyYAxisLimits(double min, double max)
        {
            if (YAxes?.Length > 0)
            {
                YAxes[0].MinLimit = min;
                YAxes[0].MaxLimit = max;
            }
        }

        private void OnCleanupTimer(object sender, EventArgs e)
        {
            lock (Sync)
            {
                if (ChartValues.Count == 0) return;
                var now = DateTime.Now;
                var window = TimeSpan.FromMinutes(TimeWindowMinutes);
                var cutoffTime = now - window - TimeSpan.FromSeconds(5);
                while (ChartValues.Count > 0 && ChartValues[0].DateTime < cutoffTime)
                    ChartValues.RemoveAt(0);
            }
        }

        [RelayCommand]
        private void ToggleAutoScale()
        {
            IsAutoScale = !IsAutoScale;
            if (!IsAutoScale)
            {
                YMin = ManualYMin;
                YMax = ManualYMax;
                TimeWindowMinutes = ManualTimeWindow;
                ApplyManualScale();
            }
        }

        [RelayCommand]
        private void ApplyManualScale()
        {
            if (ManualYMax <= ManualYMin) ManualYMax = ManualYMin + 1.0;
            if (ManualTimeWindow <= 0) ManualTimeWindow = 0.05;
            YMin = ManualYMin;
            YMax = ManualYMax;
            TimeWindowMinutes = ManualTimeWindow;
            ApplyYAxisLimits(YMin, YMax);
            UpdateXAxisLimits();
            SchedulePropertyNotifications();
        }

        [RelayCommand]
        private void ResetScale()
        {
            InitializeScaleDefaults();
            ManualYMin = YMin;
            ManualYMax = YMax;
            ManualTimeWindow = TimeWindowMinutes;
            IsAutoScale = true;
            UpdateAxesThrottled();
            SchedulePropertyNotifications();
        }

        [RelayCommand]
        private void ZoomIn()
        {
            var centerY = (YMax + YMin) / 2;
            var newRange = (YMax - YMin) * 0.7;
            ManualYMin = YMin = centerY - newRange / 2;
            ManualYMax = YMax = centerY + newRange / 2;
            ManualTimeWindow = TimeWindowMinutes = Math.Max(TimeWindowMinutes * 0.7, 0.02);
            IsAutoScale = false;
            ApplyManualScale();
        }

        [RelayCommand]
        private void ZoomOut()
        {
            var centerY = (YMax + YMin) / 2;
            var newRange = (YMax - YMin) * 1.4;
            ManualYMin = YMin = centerY - newRange / 2;
            ManualYMax = YMax = centerY + newRange / 2;
            ManualTimeWindow = TimeWindowMinutes = Math.Min(TimeWindowMinutes * 1.4, 10);
            IsAutoScale = false;
            ApplyManualScale();
        }

        [RelayCommand]
        private void ToggleScaleControls() => ShowScaleControls = !ShowScaleControls;

        [RelayCommand]
        private void ManualRefreshChart()
        {
            CreateAxes();
            UpdateAxesThrottled();
            OnPropertyChanged(nameof(ChartSeries));
            OnPropertyChanged(nameof(XAxes));
            OnPropertyChanged(nameof(YAxes));
        }

        public void Dispose()
        {
            try { _dspCts.Cancel(); _dspTask?.Wait(500); } catch { }
            _uiUpdateTimer.Tick -= OnUIUpdateTimer; _uiUpdateTimer.Stop();
            _cleanupTimer.Tick -= OnCleanupTimer; _cleanupTimer.Stop();
        }

        private struct RawSample
        {
            public DateTime TimestampUtc;
            public double Value;
        }
    }
}