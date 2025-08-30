using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Windows;
using CSharp_WPF_Websockets.Domain.Interfaces;
using CSharp_WPF_Websockets.Infrastructure.Repositories;
using CSharp_WPF_Websockets.Infrastructure.Data;
using CSharp_WPF_Websockets.Presentation.ViewModels;
using CSharp_WPF_Websockets.Presentation.Views;
using CSharp_WPF_Websockets.Application.Services;

namespace CSharp_WPF_Websockets
{
    public partial class App : System.Windows.Application
    {
        private IHost _host;
        public IServiceProvider Services => _host.Services;

        protected override void OnStartup(StartupEventArgs e)
        {
            _host = Host.CreateDefaultBuilder()
                .ConfigureServices((context, services) =>
                {
                    services.AddSingleton<IWebSocketRepository, WebSocketRepository>();
                    services.AddSingleton<SignalDataStore>();
                    services.AddSingleton<IDeviceService, DeviceService>();
                    services.AddSingleton<MainViewModel>();
                    services.AddSingleton<MainWindow>();
                    services.AddLogging(builder =>
                    {
                        builder.AddConsole();
                        builder.AddDebug();
                        builder.SetMinimumLevel(LogLevel.Debug);
                    });
                })
                .Build();

            var mainWindow = _host.Services.GetRequiredService<MainWindow>();
            mainWindow.Show();

            base.OnStartup(e);
        }

        protected override void OnExit(ExitEventArgs e)
        {
            try
            {
                _host?.Dispose();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error during app shutdown: {ex.Message}");
            }
            base.OnExit(e);
        }
    }
}