using System;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Media.Imaging;
using PhoneKey.Core.Crypto;
using PhoneKey.Core.Protocol;
using PhoneKey.Core.Storage;
using QRCoder;

namespace PhoneKey.UI.Views
{
    public partial class PairingWizardWindow : Window
    {
        private readonly DeviceRegistry _registry = new();
        private byte[] _ephemeralSharedSecret = new byte[32];
        private Guid _pcId;

        public PairingWizardWindow()
        {
            InitializeComponent();
            GeneratePairingQr();
        }

        private void GeneratePairingQr()
        {
            _pcId = PhoneKeyEnvironment.GetOrCreatePcId();
            RandomNumberGenerator.Fill(_ephemeralSharedSecret);

            using var ecdh = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
            byte[] pubKey = ecdh.ExportSubjectPublicKeyInfo();

            var payload = new EnrollmentQrPayload
            {
                PcId = _pcId.ToString(),
                PcHostname = Environment.MachineName,
                EphemeralPublicKeyBase64 = Convert.ToBase64String(pubKey),
                PairingSaltBase64 = Convert.ToBase64String(_ephemeralSharedSecret),
                CreatedAtUtc = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };

            string json = JsonSerializer.Serialize(payload);

            using var qrGenerator = new QRCodeGenerator();
            using var qrCodeData = qrGenerator.CreateQrCode(json, QRCodeGenerator.ECCLevel.M);
            using var pngQr = new PngByteQRCode(qrCodeData);
            byte[] qrBytes = pngQr.GetGraphic(20);

            using var ms = new MemoryStream(qrBytes);
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = ms;
            image.EndInit();
            image.Freeze();

            ImgQrCode.Source = image;
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void BtnComplete_Click(object sender, RoutedEventArgs e)
        {
            string password = PbPassword.Password;
            if (string.IsNullOrEmpty(password))
            {
                MessageBox.Show("Please enter your current Windows password to seal in the local DPAPI vault.", "Password Required", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                // Create enrolled phone device entry
                var dev = new EnrolledDevice
                {
                    DeviceId = Guid.NewGuid(),
                    DeviceName = "Enrolled Android Phone",
                    SharedSecretBase64 = Convert.ToBase64String(_ephemeralSharedSecret),
                    PublicKeyBase64 = "", // Populated automatically via BLE GATT handshake
                    HardwareLevel = HardwareSecurityLevel.Tee,
                    EnrolledAtUtc = DateTimeOffset.UtcNow
                };

                _registry.SaveDevice(dev);

                // Seal credential in DPAPI Vault
                string domain = Environment.UserDomainName;
                string username = Environment.UserName;
                byte[] sealedBlob = DpapiVault.SealCredential(username, domain, password, _ephemeralSharedSecret);

                string programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
                string vaultPath = Path.Combine(programData, "PhoneKey", "vault.dat");
                File.WriteAllBytes(vaultPath, sealedBlob);

                MessageBox.Show("Phone enrolled successfully! PhoneKey is now armed for passwordless unlock.", "Enrollment Complete", MessageBoxButton.OK, MessageBoxImage.Information);
                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to complete enrollment: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
