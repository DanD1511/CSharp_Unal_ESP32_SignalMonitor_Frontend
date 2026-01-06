# WS Scope - ESP32 Signal Monitor

WS Scope is a high-performance Windows Presentation Foundation (WPF) application designed to monitor, visualize, and analyze real-time signals received from microcontroller devices (specifically ESP32) via WebSockets.

This application acts as a digital oscilloscope frontend, rendering high-frequency data streams into interactive charts. It is built using .NET 8 and follows the Model-View-ViewModel (MVVM) architectural pattern to ensure code maintainability and separation of concerns.

## Features

* **Real-Time Visualization**: Renders high-frequency signal data with low latency using LiveChartsCore (SkiaSharp).
* **WebSocket Communication**: Establishes a robust, full-duplex connection with embedded devices.
* **Digital Signal Processing (DSP)**: Implements client-side Sinc interpolation to reconstruct smooth waveforms from discrete samples.
* **Dynamic Layouts**: Supports switching between 1, 2, 3, or 4-column grid views to monitor multiple signals simultaneously.
* **Interactive Charts**:
    * Automatic and manual axis scaling.
    * Zoom and Pan capabilities.
    * Real-time signal status indicators (Live, Recent, Stale).
* **Detail View**: Dedicated window for inspecting specific signals in higher resolution.
* **Modern UI**: Clean interface built with Material Design themes.

## Technical Stack

* **Framework**: .NET 8.0 (Windows)
* **Architecture**: MVVM (Model-View-ViewModel) using `CommunityToolkit.Mvvm`.
* **Charting**: `LiveChartsCore.SkiaSharpView.WPF` for hardware-accelerated rendering.
* **Styling**: `MaterialDesignThemes`.
* **Dependency Injection**: `Microsoft.Extensions.DependencyInjection`.

## Project Structure

The solution is organized following Clean Architecture principles:

* **Application**: Contains application services and business logic (e.g., `DeviceService`).
* **Domain**: Defines core entities (`DeviceSignal`, `MicroControllerDevice`) and interfaces.
* **Infrastructure**: Handles external data access, including the `WebSocketRepository` for network communication.
* **Presentation**: Contains the UI layer, including Views, ViewModels, and Converters.

## Communication Protocol

The application expects the WebSocket server to send data packets in JSON format. The endpoint typically used is `ws://<DEVICE_IP>:<PORT>/ws`.

### JSON Message Format

The server should send a JSON object containing a timestamp and an array of signals. Each signal contains metadata and a list of sample points.

```json
{
  "timestamp": "2023-10-27T10:00:00Z",
  "signals": [
    {
      "id": "sensor_1",
      "name": "Sine Wave",
      "unit": "V",
      "min": -3.3,
      "max": 3.3,
      "color": "#0078D4",
      "samples": [
        {
          "t": 0.001,
          "value": 1.25
        },
        {
          "t": 0.002,
          "value": 1.28
        }
      ]
    }
  ]
}
```

* **timestamp**: The packet timestamp (ISO 8601 string or Unix numeric timestamp).
* **signals**: An array of signal objects.
    * **samples**: An array where `t` is the relative time in seconds and `value` is the magnitude.

## Getting Started

### Prerequisites

* Visual Studio 2022 (v17.8 or later)
* .NET 8.0 SDK

### Installation and Execution

1. Clone the repository to your local machine.
2. Open the solution file `CSharp_WPF_Websockets.sln` in Visual Studio.
3. Restore the NuGet packages:
   `dotnet restore`
4. Build the solution:
   `dotnet build`
5. Run the application (F5).

### Usage

1. **Connect**: On the top header bar, enter the IP Address and Port of your WebSocket server (e.g., ESP32).
2. **Start**: Click the **Connect** button.
3. **Monitor**: Once connected, cards representing each signal will appear in the grid.
4. **Layout**: Use the layout icons in the header to change the grid column count (1 to 4 columns).
5. **Inspect**: Click the "Select" button on a card to highlight it. Use the "Open Detail Window" button (if available) to view selected signals separately.
6. **Controls**:
   * Toggle **AUTO** to switch between auto-scaling and manual scaling.
   * Use the **+** and **-** buttons to zoom in/out on the Y-axis.
