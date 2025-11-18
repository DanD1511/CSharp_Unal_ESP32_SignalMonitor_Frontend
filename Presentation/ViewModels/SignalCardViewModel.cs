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
    /// <summary>
    /// SignalCardViewModel version with a thread-safe raw buffer, DSP worker
    /// that performs windowed-sinc (bandlimited) interpolation and resamples
    /// the incoming irregular samples to a fixed 100 Hz grid. The UI is updated
    /// in batches every 200 ms.
    ///
    /// Notes:
    /// - This class intends to be drop-in as a replacement for the previous
    ///   viewmodel. It keeps the LiveCharts hookup but changes the data pipeline.
    /// - All timestamps MUST come from the server (DeviceSignal.Timestamp).
    /// - The sinc interpolation uses a Lanczos window to limit kernel aliasing.
    ///
    /// Divine commentary: The code is blessed with careful locking and respectful
    /// humor. Use wisely and may your waves be smooth.
    /// </summary>
    public partial class SignalCardViewModel : ObservableObject, IDisposable
    {
        // ---------- Original properties & fields (kept for UI compatibility) ----------
        private DeviceSignal _deviceSignal;

        // UI throttling and timers
        private readonly DispatcherTimer _uiUpdateTimer;        // used to coalesce UI property notifications
        private readonly DispatcherTimer _cleanupTimer;         // remove old points
        private bool _pendingUIUpdate = false;
        private bool _pendingChartUpdate = false;
        private readonly object _uiUpdateLock = new object();

        // Chart throttling
        private DateTime _lastChartUpdate = DateTime.MinValue;
        private readonly TimeSpan _chartUpdateThreshold = TimeSpan.FromMilliseconds(50); // 20 FPS internal axis updates

        public ObservableCollection<ISeries> ChartSeries { get; } = new();
        public ObservableCollection<DateTimePoint> ChartValues { get; } = new();

        [ObservableProperty] private Axis[] _xAxes;
        [ObservableProperty] private Axis[] _yAxes;
        [ObservableProperty] private bool _isSelected = false;
        [ObservableProperty] private LineSeries<DateTimePoint> _lineSeriesChart;

        // ===== scaling properties =====
        [ObservableProperty] private bool _isAutoScale = true;
        [ObservableProperty] private double _yMin = 0;
        [ObservableProperty] private double _yMax = 3.3;
        [ObservableProperty] private double _timeWindowMinutes = 2;
        [ObservableProperty] private bool _showScaleControls = false;

        [ObservableProperty] private double _manualYMin = 0;
        [ObservableProperty] private double _manualYMax = 3.3;
        [ObservableProperty] private double _manualTimeWindow = 2;

        public object Sync { get; } = new object();

        // ---------- NEW: DSP pipeline fields ----------
        // Raw sample queue filled by UpdateSignal (producer)
        private readonly ConcurrentQueue<RawSample> _rawQueue = new();

        // Resampled output queue (produced by DSP thread, consumed by UI timer)
        private readonly ConcurrentQueue<DateTimePoint> _resampledQueue = new();

        // DSP Worker cancellation
        private readonly CancellationTokenSource _dspCts = new();
        private Task _dspTask;

        // Resampling configuration
        private readonly double _targetSampleRate = 100.0; // Hz (as requested)
        private readonly double _targetSamplePeriodMs;

        // Sinc kernel parameters
        private readonly double _sincCutoffHz; // cutoff frequency (Hz)
        private readonly double _kernelHalfWidthSec; // how many seconds each side we use
        private readonly int _kernelHalfWidthSamplesApprox; // convenience estimate

        // Keep track of last resample time to avoid overlap
        private DateTime _lastResampleTime = DateTime.MinValue;

        // For diagnostics
        private long _totalRawReceived = 0;
        private long _totalResampledProduced = 0;

        // Constructor
        public SignalCardViewModel(DeviceSignal deviceSignal)
        {
            _deviceSignal = deviceSignal ?? throw new ArgumentNullException(nameof(deviceSignal));

            // default params
            _targetSamplePeriodMs = 1000.0 / _targetSampleRate; // 10ms for 100Hz
            _sincCutoffHz = 0.5 * _targetSampleRate; // Nyquist for target (50Hz) - typical low-pass cutoff
            _kernelHalfWidthSec = 0.06; // ±60 ms window -> reasonable for 60Hz content
            _kernelHalfWidthSamplesApprox = (int)Math.Ceiling(_kernelHalfWidthSec * 1000.0 / 1.0); // rough

            // UI update timer (batch UI notifications)
            _uiUpdateTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(200) // send to UI every 200ms
            };
            _uiUpdateTimer.Tick += OnUIUpdateTimer;

            // Cleanup timer removes old points every 2 seconds
            _cleanupTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromSeconds(2)
            };
            _cleanupTimer.Tick += OnCleanupTimer;
            _cleanupTimer.Start();

            InitializeChart();
            InitializeScaleDefaults();

            // Start DSP thread
            _dspTask = Task.Run(() => DspWorkerLoopAsync(_dspCts.Token));

            Debug.WriteLine($"[{_deviceSignal.Name}] DSP pipeline started - targetRate={_targetSampleRate}Hz, cutoff={_sincCutoffHz}Hz, kernelHalfWidth={_kernelHalfWidthSec}s");
        }

        // ------------------- Original view model properties (kept mostly intact) -------------------
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

        // ------------------- Chart initialization -------------------
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
                EnableNullSplitting = false
            };

            ChartSeries.Clear();
            ChartSeries.Add(LineSeriesChart);
        }

        private void InitializeScaleDefaults()
        {
            var currentValue = _deviceSignal.Value;

            _manualYMin = _yMin;
            _manualYMax = _yMax;
            _manualTimeWindow = _timeWindowMinutes;

            CreateAxes();
        }

        private TimeSpan GetTimeStep()
        {
            // Paso recomendado para señales rápidas o lentas:
            return TimeSpan.FromMilliseconds(50); // 20 FPS en el eje
        }


        private Func<DateTime, string> GetTimeFormatter()
        {
            return date => date.ToString("HH:mm:ss.fff");
        }


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
                    TextSize = 8,
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
                    TextSize = 8,
                    MinLimit = _yMin,
                    MaxLimit = _yMax,
                    ForceStepToMin = false,
                    IsVisible = true
                }
            };
        }

        // ------------------- New: Add raw sample to queue -------------------
        /// <summary>
        /// Called by repository when a DeviceSignal arrives. We ENQUEUE the raw sample
        /// (timestamp from server MUST be used). We do NOT discard samples here.
        /// </summary>
        public void UpdateSignal(DeviceSignal newSignal)
        {
            if (newSignal == null) return;

            // Actualizamos metadata (nombre, unidad, color)
            _deviceSignal = newSignal;

            // Timestamp base del paquete
            DateTime baseTsUtc = newSignal.Timestamp.ToUniversalTime();

            // Cada signal trae varios samples
            if (newSignal.Samples != null)
            {
                foreach (var sp in newSignal.Samples)
                {
                    // El tiempo T viene en segundos relativos
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

            // Despertar el UI coalesced update
            lock (_uiUpdateLock)
            {
                _pendingUIUpdate = true;
                if (!_uiUpdateTimer.IsEnabled)
                    _uiUpdateTimer.Start();
            }
        }


        // ------------------- DSP worker loop -------------------
        private async Task DspWorkerLoopAsync(CancellationToken ct)
        {
            // We'll resample at fixed 100Hz grid. We'll produce resampled points continuously
            // for the visible window. To avoid producing excessive points we will produce
            // resampled samples up to "now - smallLatency" where smallLatency is e.g. 20ms.

            var smallLatency = TimeSpan.FromMilliseconds(20);
            _lastResampleTime = DateTime.UtcNow; // start

            // Buffer used to accumulate raw samples dequeued from _rawQueue
            var rawList = new System.Collections.Generic.List<RawSample>();

            try
            {
                while (!ct.IsCancellationRequested)
                {
                    // Dequeue all available raw samples quickly
                    rawList.Clear();
                    while (_rawQueue.TryDequeue(out var s))
                    {
                        rawList.Add(s);
                    }

                    // If we have no new raw samples, short sleep and continue
                    if (rawList.Count == 0)
                    {
                        await Task.Delay(5, ct).ConfigureAwait(false);
                        continue;
                    }

                    // We append the raw samples to an internal history list used for interpolation.
                    // To avoid unbounded growth we keep only a few seconds worth (e.g., 3s)
                    AppendToHistory(rawList);

                    // Compute how far forward we should resample: up to now - smallLatency
                    var resampleUpTo = DateTime.UtcNow - smallLatency;

                    // Determine next resample time aligned to target rate
                    if (_lastResampleTime == DateTime.MinValue)
                        _lastResampleTime = resampleUpTo - TimeSpan.FromMilliseconds(_targetSamplePeriodMs);

                    var nextTime = _lastResampleTime + TimeSpan.FromMilliseconds(_targetSamplePeriodMs);

                    // Produce resampled points from nextTime up to resampleUpTo
                    var produced = 0;
                    while (nextTime <= resampleUpTo)
                    {
                        // Perform sinc-based interpolation at nextTime (UTC)
                        var value = SincInterpolate(nextTime);

                        // Create DateTimePoint using local kind for charting (convert to local)
                        var point = new DateTimePoint(nextTime.ToLocalTime(), value);
                        _resampledQueue.Enqueue(point);
                        Interlocked.Increment(ref _totalResampledProduced);
                        produced++;

                        _lastResampleTime = nextTime;
                        nextTime = _lastResampleTime + TimeSpan.FromMilliseconds(_targetSamplePeriodMs);
                    }

                    if (produced == 0)
                    {
                        // nothing to produce; small delay
                        await Task.Delay(3, ct).ConfigureAwait(false);
                    }
                    else
                    {
                        // if we produced samples, yield briefly
                        await Task.Delay(1, ct).ConfigureAwait(false);
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Debug.WriteLine($"[DSP] Unexpected error: {ex}");
            }
        }

        // ------------------- Internal history for interpolation -------------------
        // We maintain a short history of raw samples (UTC timestamps)
        private readonly System.Collections.Generic.List<RawSample> _history = new();
        private readonly object _historyLock = new();
        private void AppendToHistory(System.Collections.Generic.List<RawSample> incoming)
        {
            lock (_historyLock)
            {
                // Append and keep sorted by time
                _history.AddRange(incoming);
                _history.Sort((a, b) => a.TimestampUtc.CompareTo(b.TimestampUtc));

                // Keep only last 3 seconds of history
                var cutoff = DateTime.UtcNow - TimeSpan.FromSeconds(3);
                int removeCount = _history.TakeWhile(s => s.TimestampUtc < cutoff).Count();
                if (removeCount > 0)
                {
                    _history.RemoveRange(0, removeCount);
                }
            }
        }

        // ------------------- Windowed-Sinc interpolation -------------------
        // We implement a Lanczos-windowed sinc interpolation that works with irregular
        // sample times: value(t) = sum_{i in history} sample_i * sinc(2*pi*fc*(t - ti)) * lanczos((t - ti)/a)
        // where a is the Lanczos parameter (kernel half-width in samples). We limit the
        // sum to samples within +-kernelHalfWidthSec.
        private double SincInterpolate(DateTime targetUtc)
        {
            var t = targetUtc.ToUniversalTime();
            double tSec = (t - DateTime.UnixEpoch).TotalSeconds; // seconds since epoch

            // local copy of history
            RawSample[] hist;
            lock (_historyLock)
            {
                hist = _history.ToArray();
            }

            if (hist.Length == 0)
                return _deviceSignal.Value; // fallback

            // Choose cutoff and lanczos parameter
            double fc = _sincCutoffHz; // Hz
            double a = _kernelHalfWidthSec; // seconds

            // Accumulate weighted sum
            double sum = 0.0;
            double wsum = 0.0;

            // Convert each sample timestamp to seconds
            for (int i = 0; i < hist.Length; i++)
            {
                var si = hist[i];
                double siSec = (si.TimestampUtc - DateTime.UnixEpoch).TotalSeconds;
                double dt = tSec - siSec; // seconds

                if (Math.Abs(dt) > a) continue; // outside kernel window

                double x = 2.0 * Math.PI * fc * dt; // radian argument for sinc

                double sinc = SafeSinc(x);

                // Lanczos window: lanczos(u) = sinc(pi * u) / (pi * u) where u = dt / a
                double u = dt / a;
                double lanczos = SafeLanczos(u);

                double w = sinc * lanczos;
                sum += si.Value * w;
                wsum += Math.Abs(w);
            }

            if (wsum <= 1e-12) // no contributing samples -> fallback to nearest
            {
                var nearest = hist.OrderBy(h => Math.Abs((h.TimestampUtc - t).TotalMilliseconds)).First();
                return nearest.Value;
            }

            // Normalize
            return sum / wsum;
        }

        private static double SafeSinc(double x)
        {
            // sinc(x) = sin(x)/x; here x is already in radians
            if (Math.Abs(x) < 1e-8) return 1.0;
            return Math.Sin(x) / x;
        }

        private static double SafeLanczos(double u)
        {
            // lanczos kernel parameter a is encoded by caller via windowHalfWidth
            // lanczos(u) defined for |u| <= 1.0
            if (Math.Abs(u) > 1.0) return 0.0;
            if (Math.Abs(u) < 1e-8) return 1.0;
            double piU = Math.PI * u;
            return Math.Sin(piU) / piU;
        }

        // ------------------- UI timer tick: drain resampled queue and append to ChartValues in batch -------------------
        private void OnUIUpdateTimer(object sender, EventArgs e)
        {
            // Drain resampled queue into ChartValues in a single lock
            var any = false;
            lock (Sync)
            {
                while (_resampledQueue.TryDequeue(out var p))
                {
                    ChartValues.Add(p);
                    any = true;
                    _pendingChartUpdate = true;
                }

                // Keep chart history bounded by TimeWindowMinutes
                var now = DateTime.Now;
                var window = TimeSpan.FromMinutes(TimeWindowMinutes);
                var cutoff = now - window - TimeSpan.FromSeconds(5);
                while (ChartValues.Count > 0 && ChartValues[0].DateTime < cutoff)
                    ChartValues.RemoveAt(0);
            }

            if (any)
            {
                // Axis updates throttled similarly to previous behavior
                var now = DateTime.Now;
                if (now - _lastChartUpdate > _chartUpdateThreshold)
                {
                    _lastChartUpdate = now;
                    if (System.Windows.Application.Current?.Dispatcher?.CheckAccess() == true)
                        UpdateAxesThrottled();
                    else
                        System.Windows.Application.Current?.Dispatcher?.BeginInvoke(UpdateAxesThrottled, DispatcherPriority.Background);
                }

                // Notify UI properties in a coalesced fashion
                SchedulePropertyNotifications();
            }

            // Stop the UI timer if nothing pending; it will be started again on next UpdateSignal
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

        // ------------------- Axis and Y scaling logic (kept similar to previous) -------------------
        private void UpdateAxesThrottled()
        {
            if (!_pendingChartUpdate) return;

            lock (Sync)
            {
                _pendingChartUpdate = false;
                UpdateXAxisLimits();

                if (IsAutoScale)
                    UpdateYAxisLimits();
                else
                    ApplyYAxisLimits(YMin, YMax);
            }
        }

        private void UpdateXAxisLimits()
        {
            if (XAxes?.Length > 0)
            {
                var now = DateTime.Now;
                var window = TimeSpan.FromMinutes(TimeWindowMinutes);
                var xAxis = XAxes[0];

                xAxis.MinLimit = (now - window).Ticks;
                xAxis.MaxLimit = now.Ticks;
            }
        }

        private void UpdateYAxisLimits()
        {
            if (ChartValues.Count < 5 || YAxes?.Length == 0)
                return;

            var now = DateTime.Now;
            var window = TimeSpan.FromMinutes(TimeWindowMinutes);
            var cutoffTime = now - window;

            var visibleValues = ChartValues
                .Where(p => p.DateTime >= cutoffTime)
                .Select(p => p.Value)
                .ToArray();

            if (visibleValues.Length == 0) return;

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
                var yAxis = YAxes[0];
                yAxis.MinLimit = min;
                yAxis.MaxLimit = max;
            }
        }

        // ------------------- Cleanup timer -------------------
        private void OnCleanupTimer(object sender, EventArgs e)
        {
            lock (Sync)
            {
                if (ChartValues.Count == 0) return;

                var now = DateTime.Now;
                var window = TimeSpan.FromMinutes(TimeWindowMinutes);
                var cutoffTime = now - window - TimeSpan.FromSeconds(5);

                int removedCount = 0;
                while (ChartValues.Count > 0 && ChartValues[0].DateTime < cutoffTime)
                {
                    ChartValues.RemoveAt(0);
                    removedCount++;
                }

                if (removedCount > 0)
                {
                    Debug.WriteLine($"[{_deviceSignal.Name}] Limpieza: {removedCount} puntos antiguos removidos. Total: {ChartValues.Count}");
                }
            }
        }

        // ------------------- Helpers and commands (kept mostly the same) -------------------
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
            if (ManualYMax <= ManualYMin)
                ManualYMax = ManualYMin + 1.0;

            if (ManualTimeWindow <= 0)
                ManualTimeWindow = 0.05;

            YMin = ManualYMin;
            YMax = ManualYMax;
            TimeWindowMinutes = ManualTimeWindow;

            ApplyYAxisLimits(YMin, YMax);
            UpdateXAxisLimits();
            SchedulePropertyNotifications();

            Debug.WriteLine($"[{_deviceSignal.Name}] Manual scale applied: Y({YMin:F2}-{YMax:F2}), T({TimeWindowMinutes:F2}min)");
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
        private void ToggleScaleControls()
        {
            ShowScaleControls = !ShowScaleControls;
        }

        [RelayCommand]
        private void ManualRefreshChart()
        {
            CreateAxes();
            UpdateAxesThrottled();
            OnPropertyChanged(nameof(ChartSeries));
            OnPropertyChanged(nameof(XAxes));
            OnPropertyChanged(nameof(YAxes));
            Debug.WriteLine($"[{_deviceSignal.Name}] Manual refresh completed");
        }

        public void DebugChart()
        {
            Debug.WriteLine($"=== CHART DEBUG [{_deviceSignal.Name}] ===");
            Debug.WriteLine($"Raw received: {_totalRawReceived}, Resampled produced: {_totalResampledProduced}");
            Debug.WriteLine($"ChartValues Count: {ChartValues.Count}");
            Debug.WriteLine($"AutoScale: {IsAutoScale}");
            Debug.WriteLine($"Y Limits: {YMin:F2} - {YMax:F2}");
            Debug.WriteLine($"Time Window: {TimeWindowMinutes:F2} min");
            Debug.WriteLine($"Pending Updates: Chart={_pendingChartUpdate}, UI={_pendingUIUpdate}");

            if (ChartValues.Any())
            {
                var first = ChartValues.First();
                var last = ChartValues.Last();
                Debug.WriteLine($"Data Range: {first.DateTime:HH:mm:ss.fff} ({first.Value:F3}) to {last.DateTime:HH:mm:ss.fff} ({last.Value:F3})");
            }
        }

        // ------------------- Dispose / cleanup -------------------
        public void Dispose()
        {
            try
            {
                _dspCts.Cancel();
                _dspTask?.Wait(500);
            }
            catch { }

            _uiUpdateTimer.Tick -= OnUIUpdateTimer;
            _uiUpdateTimer.Stop();

            _cleanupTimer.Tick -= OnCleanupTimer;
            _cleanupTimer.Stop();
        }

        // ------------------- Internal raw sample struct -------------------
        private struct RawSample
        {
            public DateTime TimestampUtc;
            public double Value;
        }
    }
}
