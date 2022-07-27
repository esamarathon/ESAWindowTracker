using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace ESAWindowTracker
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly IOptionsMonitor<Config> options;
        private readonly RabbitMessageSender rabbitMessageSender;

        public MainWindow(IOptionsMonitor<Config> options, RabbitMessageSender rabbitMessageSender)
        {
            InitializeComponent();

            this.options = options;
            this.rabbitMessageSender = rabbitMessageSender;

            rabbitMessageSender.StatusChanged += RabbitMessageSender_StatusChanged;
            RabbitStatusLabel.Content = rabbitMessageSender.Status;

            options.OnChange(OnConfigChange);
            OnConfigChange(options.CurrentValue, "");
        }

        private void OnConfigChange(Config cfg, string _)
        {
            IDField.Content = $"This is PC {cfg.PCID} at {cfg.EventShort}";
        }

        private void RabbitMessageSender_StatusChanged(string status)
        {
            RabbitStatusLabel.Content = status;
        }

        private void Show_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            DoShow();
        }

        private void ShowMenu_Click(object sender, RoutedEventArgs e)
        {
            DoShow();
        }

        private void DoShow()
        {
            Visibility = Visibility.Visible;
            ShowInTaskbar = true;
            Show();
            Focus();
        }

        private void ExitMenu_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            e.Cancel = true;
            Hide();
        }
    }
}
