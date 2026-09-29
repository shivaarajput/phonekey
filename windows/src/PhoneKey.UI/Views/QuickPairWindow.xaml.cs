using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using PhoneKey.Core.Ipc;

namespace PhoneKey.UI.Views
{
    public partial class QuickPairWindow : Window
    {
        private readonly ulong _discoveredAddress;

        public QuickPairWindow(ulong discoveredAddress)
        {
            InitializeComponent();
            _discoveredAddress = discoveredAddress;
            Loaded += (s, e) => PbPassword.Focus();
        }

        private void PbPassword_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                BtnConfirm_Click(this, new RoutedEventArgs());
            }
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private async void BtnConfirm_Click(object sender, RoutedEventArgs e)
        {
            string password = PbPassword.Password;
            if (string.IsNullOrEmpty(password))
            {
                MessageBox.Show("Please enter your current Windows password.", "Password Required", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            BtnConfirm.IsEnabled = false;

            try
            {
                var payload = new QuickPairPayload
                {
                    BluetoothAddress = _discoveredAddress,
                    Password = password,
                    Username = Environment.UserName,
                    Domain = Environment.UserDomainName
                };

                var req = new IpcRequest
                {
                    Command = IpcCommandType.QuickPair,
                    Payload = JsonSerializer.Serialize(payload)
                };

                bool success = await System.Threading.Tasks.Task.Run(() =>
                {
                    try
                    {
                        using var pipe = new NamedPipeClientStream(".", IpcConstants.PipeName, PipeDirection.InOut);
                        pipe.Connect(500);

                        using var writer = new StreamWriter(pipe, Encoding.UTF8, leaveOpen: true) { AutoFlush = true };
                        using var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);

                        writer.WriteLine(JsonSerializer.Serialize(req));
                        string? resp = reader.ReadLine();
                        return resp != null && resp.Contains("\"success\":true");
                    }
                    catch
                    {
                        return false;
                    }
                });

                if (success)
                {
                    MessageBox.Show("Phone paired successfully! Your PC will now unlock automatically when your phone is present, and lock when you leave.", "PhoneKey Armed", MessageBoxButton.OK, MessageBoxImage.Information);
                    DialogResult = true;
                    Close();
                }
                else
                {
                    MessageBox.Show("Failed to complete 1-Click pairing. Please make sure PhoneKeyService is running.", "Pairing Failed", MessageBoxButton.OK, MessageBoxImage.Error);
                    BtnConfirm.IsEnabled = true;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Pairing error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                BtnConfirm.IsEnabled = true;
            }
        }
    }
}
