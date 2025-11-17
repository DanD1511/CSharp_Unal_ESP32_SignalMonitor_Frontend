using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
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
    public partial class SignalCardViewModel : ObservableObject
    {
        private DeviceSignal _deviceSignal;

        // Throttling para UI updates
        private readonly DispatcherTimer _uiUpdateTimer;
        private readonly DispatcherTimer _cleanupTimer;
        private bool _pendingUIUpdate = false;
        private bool _pendingChartUpdate = false;
        private readonly object _uiUpdateLock = new object();

        // ✅ Throttling de RECEPCIÓN de datos
        private DateTime _lastDataReceived = DateTime.MinValue;
        private readonly TimeSpan _dataThrottleThreshold;

        // Throttling para chart updates
        private DateTime _lastChartUpdate = DateTime.MinValue;
        private readonly TimeSpan _chartUpdateThreshold = TimeSpan.FromMilliseconds(50); // 20 FPS

        public ObservableCollection<ISeries> ChartSeries { get; } = new();
        public ObservableCollection<DateTimePoint> ChartValues { get; } = new();

        [ObservableProperty] private Axis[] _xAxes;
        [ObservableProperty] private Axis[] _yAxes;
        [ObservableProperty] private bool _isSelected = false;
        [ObservableProperty] private LineSeries<DateTimePoint> _lineSeriesChart;

        // ===== PROPIEDADES PARA ESCALADO =====
        [ObservableProperty] private bool _isAutoScale = true;
        [ObservableProperty] private double _yMin = 0;
        [ObservableProperty] private double _yMax = 3.3;
        [ObservableProperty] private double _timeWindowMinutes = 2;
        [ObservableProperty] private bool _showScaleControls = false;

        // Propiedades para los controles manuales
        [ObservableProperty] private double _manualYMin = 0;
        [ObservableProperty] private double _manualYMax = 3.3;
        [ObservableProperty] private double _manualTimeWindow = 2;

        public object Sync { get; } = new object();

        public SignalCardViewModel(DeviceSignal deviceSignal)
        {
            _deviceSignal = deviceSignal;

            // ✅ Configurar throttling según tipo de señal
            _dataThrottleThreshold = GetDataThrottleInterval();

            // Timer para UI updates throttling
            _uiUpdateTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(200)
            };
            _uiUpdateTimer.Tick += OnUIUpdateTimer;

            // Timer para limpieza periódica de datos antiguos
            _cleanupTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromSeconds(2) // Limpiar cada 2 segundos
            };
            _cleanupTimer.Tick += OnCleanupTimer;
            _cleanupTimer.Start();

            InitializeChart();
            InitializeScaleDefaults();

            // Agregar punto inicial
            AddDataPoint(_deviceSignal.Value, _deviceSignal.Timestamp);

            Debug.WriteLine($"[{_deviceSignal.Name}] ViewModel inicializado - Throttle: {_dataThrottleThreshold.TotalMilliseconds}ms");
        }

        // ===== PROPIEDADES EXISTENTES =====
        public string Name => _deviceSignal.Name;
        public double Value => _deviceSignal.Value;
        public string Type => _deviceSignal.Type;
        public string Unit => _deviceSignal.Unit;
        public DateTime Timestamp => _deviceSignal.Timestamp;
        public string Color => _deviceSignal.Color;
        public double MinValue => _deviceSignal.MinValue;
        public double MaxValue => _deviceSignal.MaxValue;
        public DeviceSignal Signal => _deviceSignal;

        public string DisplayValue =>
            (!string.IsNullOrEmpty(_deviceSignal.Unit) && _deviceSignal.Unit.ToLower() == "bool")
                || (_deviceSignal.Type?.ToLower() is "digital" or "motion")
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

        // ✅ Determinar intervalo de throttling según tipo de señal
        private TimeSpan GetDataThrottleInterval()
        {
            return _deviceSignal.Type?.ToLower() switch
            {
                var t when t?.Contains("sine") == true => TimeSpan.FromMilliseconds(10), // 100 puntos/seg para ondas suaves
                "voltage" or "volt" or "real voltage" => TimeSpan.FromMilliseconds(200), // 5 puntos/seg
                _ => TimeSpan.FromMilliseconds(100) // 10 puntos/seg por defecto
            };
        }

        private void InitializeChart()
        {
            var signalColor = GetSignalColor();

            LineSeriesChart = new LineSeries<DateTimePoint>
            {
                Values = ChartValues,
                Name = _deviceSignal.Name ?? _deviceSignal.Type,
                Stroke = new SolidColorPaint(signalColor, 2),
                Fill = null,
                GeometryStroke = null,
                GeometryFill = null,
                GeometrySize = GetGeometrySize(),
                LineSmoothness = GetLineSmoothness(),
                AnimationsSpeed = TimeSpan.Zero,
                EnableNullSplitting = false
            };

            ChartSeries.Clear();
            ChartSeries.Add(LineSeriesChart);

            Debug.WriteLine($"[{_deviceSignal.Name}] Chart initialized - Smoothness: {GetLineSmoothness()}");
        }

        private void InitializeScaleDefaults()
        {
            var currentValue = _deviceSignal.Value;

            // Definir rangos sensatos basados en el tipo de señal
            switch (_deviceSignal.Type?.ToLower())
            {
                case var t when t?.Contains("sine") == true:
                    _yMin = 0;
                    _yMax = 3.3;
                    _timeWindowMinutes = 0.0083; // 0.5 segundos (medio segundo) para ver varios ciclos
                    break;

                case "voltage":
                case "volt":
                case "real voltage":
                    _yMin = 0;
                    _yMax = 3.5;
                    _timeWindowMinutes = 2;
                    break;

                case "adc":
                    _yMin = 0;
                    _yMax = 1023;
                    _timeWindowMinutes = 2;
                    break;

                default:
                    _yMin = Math.Max(0, currentValue - 2);
                    _yMax = currentValue + 2;
                    if (_yMax <= _yMin) _yMax = _yMin + 3.3;
                    _timeWindowMinutes = 2;
                    break;
            }

            // Sincronizar con controles manuales
            _manualYMin = _yMin;
            _manualYMax = _yMax;
            _manualTimeWindow = _timeWindowMinutes;

            CreateAxes();

            Debug.WriteLine($"[{_deviceSignal.Name}] Escala inicial: Y({_yMin:F2} - {_yMax:F2}), T({_timeWindowMinutes:F2}min)");
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
                    Name = GetYAxisLabel(),
                    LabelsPaint = new SolidColorPaint(SKColors.LightGray),
                    TextSize = 8,
                    MinLimit = _yMin,
                    MaxLimit = _yMax,
                    ForceStepToMin = false,
                    IsVisible = true
                }
            };

            Debug.WriteLine($"[{_deviceSignal.Name}] Ejes creados - Y: {_yMin:F2} to {_yMax:F2}");
        }

        private TimeSpan GetTimeStep()
        {
            return _deviceSignal.Type?.ToLower() switch
            {
                var t when t?.Contains("sine") == true => TimeSpan.FromMilliseconds(50), // Marcas cada 50ms
                _ => TimeSpan.FromSeconds(30)
            };
        }

        private Func<DateTime, string> GetTimeFormatter()
        {
            return _deviceSignal.Type?.ToLower() switch
            {
                var t when t?.Contains("sine") == true => date => date.ToString("ss.fff"), // Solo segundos.milisegundos
                _ => date => date.ToString("HH:mm:ss")
            };
        }

        private double GetGeometrySize()
        {
            return _deviceSignal.Type?.ToLower() switch
            {
                var t when t?.Contains("sine") == true => 0,
                _ => 2
            };
        }

        private double GetLineSmoothness()
        {
            return _deviceSignal.Type?.ToLower() switch
            {
                var t when t?.Contains("sine") == true => 0, // ✅ SIN suavizado - líneas rectas entre puntos
                _ => 0
            };
        }

        private SKColor GetSignalColor()
        {
            if (!string.IsNullOrEmpty(_deviceSignal.Color))
            {
                try
                {
                    var colorStr = _deviceSignal.Color.TrimStart('#');
                    if (uint.TryParse(colorStr, System.Globalization.NumberStyles.HexNumber, null, out uint colorValue))
                    {
                        return new SKColor(
                            (byte)((colorValue >> 16) & 0xFF),
                            (byte)((colorValue >> 8) & 0xFF),
                            (byte)(colorValue & 0xFF));
                    }
                }
                catch { }
            }

            return _deviceSignal.Type?.ToLower() switch
            {
                "adc" => SKColors.Orange,
                "volt" or "voltage" or "real voltage" => SKColors.DeepSkyBlue,
                var t when t?.Contains("sine") == true => SKColors.Red,
                "light" => SKColors.Yellow,
                "motion" => SKColors.Purple,
                "digital" => SKColors.Green,
                _ => SKColors.LightBlue
            };
        }

        private string GetYAxisLabel() =>
            !string.IsNullOrEmpty(_deviceSignal.Unit) ? _deviceSignal.Unit :
            _deviceSignal.Type?.ToLower() switch
            {
                "adc" => "ADC",
                "volt" or "voltage" or "real voltage" => "V",
                var t when t?.Contains("sine") == true => "V",
                "light" => "Lux",
                "motion" => "State",
                "digital" => "State",
                _ => "Value"
            };

        private void AddDataPoint(double value, DateTime timestamp)
        {
            if (timestamp == default)
                timestamp = DateTime.Now;

            var dataPoint = new DateTimePoint(timestamp, value);

            lock (Sync)
            {
                ChartValues.Add(dataPoint);
                _pendingChartUpdate = true;
            }

            // Throttle chart updates basado en tiempo
            var now = DateTime.Now;
            if (now - _lastChartUpdate > _chartUpdateThreshold)
            {
                _lastChartUpdate = now;

                if (System.Windows.Application.Current?.Dispatcher?.CheckAccess() == true)
                    UpdateAxesThrottled();
                else
                    System.Windows.Application.Current?.Dispatcher?.BeginInvoke(UpdateAxesThrottled, DispatcherPriority.Background);
            }

            // Debug ocasional
            if (ChartValues.Count % 100 == 0)
            {
                Debug.WriteLine($"[{_deviceSignal.Name}] Puntos: {ChartValues.Count}, Último: {value:F2}");
            }
        }

        // Timer para limpieza periódica de datos antiguos
        private void OnCleanupTimer(object sender, EventArgs e)
        {
            lock (Sync)
            {
                if (ChartValues.Count == 0) return;

                var now = DateTime.Now;
                var window = TimeSpan.FromMinutes(TimeWindowMinutes);
                var cutoffTime = now - window - TimeSpan.FromSeconds(5); // Buffer adicional

                // Remover puntos más antiguos que la ventana visible
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

            // Solo considerar puntos dentro de la ventana visible
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

            // Manejar caso de valores constantes
            if (range < 0.01)
            {
                range = Math.Max(0.5, Math.Abs((double)min) * 0.2);
                min -= range / 2;
                max += range / 2;
            }

            // Agregar padding
            var padding = range * 0.15;
            var newYMin = min - padding;
            var newYMax = max + padding;

            // Aplicar límites del dispositivo si existen
            if (_deviceSignal.MinValue < _deviceSignal.MaxValue)
            {
                newYMin = Math.Max((double)newYMin, _deviceSignal.MinValue);
                newYMax = Math.Min((double)newYMax, _deviceSignal.MaxValue);
            }

            // Solo actualizar si el cambio es significativo
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

        // Timer para UI updates throttled
        private void OnUIUpdateTimer(object sender, EventArgs e)
        {
            lock (_uiUpdateLock)
            {
                if (_pendingUIUpdate)
                {
                    _pendingUIUpdate = false;
                    _uiUpdateTimer.Stop();

                    // Actualizar propiedades de una vez
                    OnPropertyChanged(nameof(XAxes));
                    OnPropertyChanged(nameof(YAxes));
                    NotifyPropertiesChanged();
                }
            }
        }

        private void ScheduleUIUpdate()
        {
            lock (_uiUpdateLock)
            {
                _pendingUIUpdate = true;
                if (!_uiUpdateTimer.IsEnabled)
                {
                    _uiUpdateTimer.Start();
                }
            }
        }

        // ===== COMANDOS =====
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
            // Validaciones
            if (ManualYMax <= ManualYMin)
                ManualYMax = ManualYMin + 1.0;

            if (ManualTimeWindow <= 0)
                ManualTimeWindow = 0.05;

            YMin = ManualYMin;
            YMax = ManualYMax;
            TimeWindowMinutes = ManualTimeWindow;

            ApplyYAxisLimits(YMin, YMax);
            UpdateXAxisLimits();
            ScheduleUIUpdate();

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
            ScheduleUIUpdate();
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

        // ✅ UpdateSignal con throttling de recepción
        public void UpdateSignal(DeviceSignal newSignal)
        {
            var oldValue = _deviceSignal.Value;
            var oldTimestamp = _deviceSignal.Timestamp;

            _deviceSignal = newSignal;

            // ✅ Aplicar throttling de recepción
            var now = DateTime.Now;
            var timeSinceLastData = now - _lastDataReceived;

            if (timeSinceLastData < _dataThrottleThreshold && ChartValues.Count > 0)
            {
                // Descartar esta muestra para no saturar
                return;
            }

            _lastDataReceived = now;

            // Agregar el punto
            AddDataPoint(newSignal.Value, newSignal.Timestamp);

            // Throttled UI update
            ScheduleUIUpdate();
        }

        private void NotifyPropertiesChanged()
        {
            OnPropertyChanged(nameof(Value));
            OnPropertyChanged(nameof(DisplayValue));
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(StatusColor));
            OnPropertyChanged(nameof(Timestamp));
        }

        public void DebugChart()
        {
            Debug.WriteLine($"=== CHART DEBUG [{_deviceSignal.Name}] ===");
            Debug.WriteLine($"ChartValues Count: {ChartValues.Count}");
            Debug.WriteLine($"ChartSeries Count: {ChartSeries.Count}");
            Debug.WriteLine($"AutoScale: {IsAutoScale}");
            Debug.WriteLine($"Y Limits: {YMin:F2} - {YMax:F2}");
            Debug.WriteLine($"Time Window: {TimeWindowMinutes:F2} min");
            Debug.WriteLine($"Pending Updates: Chart={_pendingChartUpdate}, UI={_pendingUIUpdate}");
            Debug.WriteLine($"Data Throttle: {_dataThrottleThreshold.TotalMilliseconds}ms");

            if (ChartValues.Any())
            {
                var first = ChartValues.First();
                var last = ChartValues.Last();
                Debug.WriteLine($"Data Range: {first.DateTime:HH:mm:ss.fff} ({first.Value:F3}) to {last.DateTime:HH:mm:ss.fff} ({last.Value:F3})");
            }
        }

        // Cleanup
        public void Dispose()
        {
            if (_uiUpdateTimer != null)
            {
                _uiUpdateTimer.Tick -= OnUIUpdateTimer;
                _uiUpdateTimer.Stop();
            }

            if (_cleanupTimer != null)
            {
                _cleanupTimer.Tick -= OnCleanupTimer;
                _cleanupTimer.Stop();
            }
        }
    }
}