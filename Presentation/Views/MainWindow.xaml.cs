using Microsoft.Extensions.DependencyInjection;
using System.ComponentModel;
using System.Windows;
using CSharp_WPF_Websockets.Presentation.ViewModels;

namespace CSharp_WPF_Websockets.Presentation.Views
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();

            try
            {
                var app = (App)System.Windows.Application.Current;
                DataContext = app.Services.GetRequiredService<MainViewModel>();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error initializing application: {ex.Message}",
                              "Initialization Error",
                              MessageBoxButton.OK,
                              MessageBoxImage.Error);
                Close();
            }
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            try
            {
                var viewModel = DataContext as MainViewModel;
                if (viewModel?.IsConnected == true)
                {
                    viewModel.DisconnectCommand.Execute(null);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error during disconnect: {ex.Message}");
            }

            base.OnClosing(e);
        }
    }
}
