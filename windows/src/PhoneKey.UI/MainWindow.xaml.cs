using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using PhoneKey.Core.Ipc;
using PhoneKey.Core.Proximity;
using PhoneKey.Core.Storage;
using PhoneKey.UI.Views;

namespace PhoneKey.UI
{
    public partial class MainWindow : Window
    {
        private readonly DispatcherTimer _pollTimer;
        private readonly DeviceRegistry _deviceRegistry = new();
        private bool _isPolling;
        private ulong _lastDiscoveredAddress;

        public MainWindow()
        {
            InitializeComponent();

            _pollTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(800)
            };
            _pollTimer.Tick += async (s, e) => await PollServiceStatusAsync();
            _pollTimer.Start();
        }

        private async Task PollServiceStatusAsync()
        {
            if (_isPolling) return;
            _isPolling = true;

            try
            {
                var status = await QueryServiceStatusAsync();
                if (status != null)
                {
                    UpdateUi(status);
                }
                else
                {
                    SetServiceOfflineUi();
                }
            }
            finally
            {
                _isPolling = false;
            }
        }

        private async Task<IpcStatusResponse?> QueryServiceStatusAsync()
        {
            return await Task.Run(() =>
            {
                try
                {
                    using var pipe = new NamedPipeClientStream(".", IpcConstants.PipeName, PipeDirection.InOut);
                    pipe.Connect(300);

                    using var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);
                    using var writer = new StreamWriter(pipe, Encoding.UTF8, leaveOpen: true) { AutoFlush = true };

                    writer.WriteLine("{\"Command\":1}");
                    string? respJson = reader.ReadLine();
                    if (string.IsNullOrEmpty(respJson)) return null;

                    return JsonSerializer.Deserialize<IpcStatusResponse>(respJson);
                }
                catch
                {
                    return null;
                }
            });
        }

        private void UpdateUi(IpcStatusResponse status)
        {
            ServiceStatusDot.Fill = (SolidColorBrush)FindResource("BrushSuccess");
            TxtServiceStatus.Text = "Service: Active";

            // 1-Click Nearby Discovered Phone Banner
            if (status.HasDiscoveredPhone && !status.IsPhoneConnected && _deviceRegistry.GetDevices().Count == 0)
            {
                BannerNearbyPhone.Visibility = Visibility.Visible;
                TxtNearbyPhoneInfo.Text = $"Found Phone (Signal: {status.DiscoveredPhoneRssi} dBm). Click to pair and unlock your PC automatically when present.";
                _lastDiscoveredAddress = status.DiscoveredPhoneAddress;
            }
            else
            {
                BannerNearbyPhone.Visibility = Visibility.Collapsed;
            }

            // Phone Connection
            if (status.IsPhoneConnected)
            {
                PhoneStatusDot.Fill = (SolidColorBrush)FindResource("BrushSuccess");
                TxtPhoneStatus.Text = string.IsNullOrEmpty(status.ConnectedPhoneName) ? "Phone Connected" : status.ConnectedPhoneName;
                TxtPhoneModel.Text = $"Status: Active | Battery: {status.BatteryPercent}%";
            }
            else
            {
                PhoneStatusDot.Fill = (SolidColorBrush)FindResource("BrushWarning");
                TxtPhoneStatus.Text = "Scanning for phone...";
                TxtPhoneModel.Text = "BLE Peripheral: None";
            }

            // Cryptographic Auth
            if (status.IsAuthenticated)
            {
                TxtAuthStatus.Text = "Cryptographically Verified";
                TxtAuthStatus.Foreground = (SolidColorBrush)FindResource("BrushSuccess");
                TxtHardwareSecurity.Text = $"Hardware: {status.HardwareSecurityLevel} (Hardware-Backed)";
            }
            else
            {
                TxtAuthStatus.Text = "Pending Challenge";
                TxtAuthStatus.Foreground = (SolidColorBrush)FindResource("BrushWarning");
                TxtHardwareSecurity.Text = "Hardware: Unknown";
            }

            // Proximity
            if (status.IsInProximity)
            {
                TxtProximityStatus.Text = "Nearby (In Proximity)";
                TxtProximityStatus.Foreground = (SolidColorBrush)FindResource("BrushSuccess");
            }
            else if (status.CurrentProximityState == ProximityState.GracePeriod)
            {
                TxtProximityStatus.Text = $"Grace Period ({status.GracePeriodRemainingSeconds}s)";
                TxtProximityStatus.Foreground = (SolidColorBrush)FindResource("BrushDanger");
            }
            else
            {
                TxtProximityStatus.Text = "Out of Range";
                TxtProximityStatus.Foreground = (SolidColorBrush)FindResource("BrushTextSecondary");
            }

            TxtRssiValue.Text = $" ({status.FilteredRssi:0.0} dBm)";
            
            // Map RSSI (-100 to -40) to Progress (0 to 100)
            double mappedProgress = Math.Clamp((status.FilteredRssi + 100) * 1.66, 0, 100);
            RssiProgressBar.Value = mappedProgress;

            if (status.CurrentProximityState == ProximityState.GracePeriod)
            {
                TxtGracePeriod.Text = $"Locking workstation in {status.GracePeriodRemainingSeconds} seconds...";
            }
            else
            {
                TxtGracePeriod.Text = status.IsInProximity ? "Continuous heartbeat verified." : "Auto-lock grace period: Idle";
            }
        }

        private void SetServiceOfflineUi()
        {
            BannerNearbyPhone.Visibility = Visibility.Collapsed;
            ServiceStatusDot.Fill = (SolidColorBrush)FindResource("BrushDanger");
            TxtServiceStatus.Text = "Service: Stopped";
            PhoneStatusDot.Fill = (SolidColorBrush)FindResource("BrushDanger");
            TxtPhoneStatus.Text = "Service Disconnected";
            TxtPhoneModel.Text = "Cannot reach PhoneKey Windows Service.";
            TxtAuthStatus.Text = "Inactive";
            TxtAuthStatus.Foreground = (SolidColorBrush)FindResource("BrushDanger");
            TxtProximityStatus.Text = "Offline";
            TxtRssiValue.Text = "";
            RssiProgressBar.Value = 0;
            TxtGracePeriod.Text = "Ensure PhoneKeyService is started.";
        }

        private void BtnQuickPair_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new QuickPairWindow(_lastDiscoveredAddress) { Owner = this };
            dlg.ShowDialog();
        }

        private async void BtnLockNow_Click(object sender, RoutedEventArgs e)
        {
            await Task.Run(() =>
            {
                try
                {
                    using var pipe = new NamedPipeClientStream(".", IpcConstants.PipeName, PipeDirection.InOut);
                    pipe.Connect(300);
                    using var writer = new StreamWriter(pipe, Encoding.UTF8, leaveOpen: true) { AutoFlush = true };
                    writer.WriteLine("{\"Command\":3}");
                }
                catch { }
            });
        }

        private void BtnRegisterPhone_Click(object sender, RoutedEventArgs e)
        {
            var wizard = new PairingWizardWindow { Owner = this };
            wizard.ShowDialog();
        }

        private void BtnRemovePhone_Click(object sender, RoutedEventArgs e)
        {
            var devices = _deviceRegistry.GetDevices();
            if (devices.Count == 0)
            {
                MessageBox.Show("No phone is currently registered with this PC.", "PhoneKey", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var result = MessageBox.Show(
                $"Are you sure you want to remove and revoke '{devices[0].DeviceName}'?\n\nThis phone will immediately lose ability to unlock this PC.",
                "Revoke Registered Phone",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                _deviceRegistry.ClearAll();
                MessageBox.Show("Phone revoked successfully. Previous credentials have been wiped.", "Revoked", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void BtnSettings_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("Settings are automatically saved and applied in real-time.", "PhoneKey Settings", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void BtnDiagnostics_Click(object sender, RoutedEventArgs e)
        {
            var diag = new DiagnosticsDialog { Owner = this };
            diag.ShowDialog();
        }
    }
}
