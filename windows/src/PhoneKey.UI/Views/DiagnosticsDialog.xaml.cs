using System;
using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Text;
using System.Windows;
using PhoneKey.Core.Storage;

namespace PhoneKey.UI.Views
{
    public partial class DiagnosticsDialog : Window
    {
        public DiagnosticsDialog()
        {
            InitializeComponent();
            LoadDiagnostics();
        }

        private void LoadDiagnostics()
        {
            var sb = new StringBuilder();
            sb.AppendLine("=======================================================");
            sb.AppendLine("           PHONEKEY SUBSYSTEM DIAGNOSTIC AUDIT         ");
            sb.AppendLine("=======================================================");
            sb.AppendLine($"Timestamp (UTC): {DateTimeOffset.UtcNow:O}");
            sb.AppendLine($"OS Version: {Environment.OSVersion}");
            sb.AppendLine($"Machine Name: {Environment.MachineName}");
            sb.AppendLine($"Current User: {WindowsIdentity.GetCurrent().Name}");
            sb.AppendLine($"Process ID: {Process.GetCurrentProcess().Id}");
            sb.AppendLine($"Session ID: {Process.GetCurrentProcess().SessionId}");
            sb.AppendLine($"Is 64-Bit OS: {Environment.Is64BitOperatingSystem}");
            sb.AppendLine($"Is 64-Bit Process: {Environment.Is64BitProcess}");
            sb.AppendLine();

            sb.AppendLine("--- CRYPTOGRAPHIC CONFIGURATION ---");
            sb.AppendLine("Asymmetric Primitive: ECDSA NIST P-256 (secp256r1)");
            sb.AppendLine("Digest Algorithm: SHA-256");
            sb.AppendLine("Challenge Size: 256 bits (CSPRNG CryptGenRandom)");
            sb.AppendLine("Relay Mitigation Max RTT: 400 milliseconds");
            sb.AppendLine("Vault Protection: Windows DPAPI (LocalMachine) + AES-256-GCM");
            sb.AppendLine();

            sb.AppendLine("--- LOCAL SECURE STORAGE & VAULT ---");
            string programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            string appDir = Path.Combine(programData, "PhoneKey");
            string vaultPath = Path.Combine(appDir, "vault.dat");
            string devicesPath = Path.Combine(appDir, "devices.dat");

            sb.AppendLine($"Storage Directory: {appDir}");
            sb.AppendLine($"Credential Vault Present: {File.Exists(vaultPath)}");
            if (File.Exists(vaultPath))
            {
                var fi = new FileInfo(vaultPath);
                sb.AppendLine($"Credential Vault Size: {fi.Length} bytes (Sealed Ciphertext)");
            }

            var registry = new DeviceRegistry();
            var devices = registry.GetDevices();
            sb.AppendLine($"Enrolled Phones Count: {devices.Count}");
            foreach (var d in devices)
            {
                sb.AppendLine($"  - Device ID: {d.DeviceId}");
                sb.AppendLine($"    Name: {d.DeviceName}");
                sb.AppendLine($"    Hardware Backing: {d.HardwareLevel}");
                sb.AppendLine($"    Enrolled At: {d.EnrolledAtUtc:g}");
                sb.AppendLine($"    Last Authenticated: {d.LastAuthenticatedUtc:g}");
                sb.AppendLine($"    Is Revoked: {d.IsRevoked}");
            }
            sb.AppendLine();

            sb.AppendLine("--- WINDOWS CREDENTIAL PROVIDER ---");
            sb.AppendLine("CLSID: {8E79B5A2-7D3C-4D2A-98C1-F04E51C2D901}");
            sb.AppendLine("Module: PhoneKeyCP.dll");
            sb.AppendLine("Logon Package: KerbInteractiveLogon / MSV1_0 (Negotiate SSP)");
            sb.AppendLine("IPC Pipe: \\\\.\\pipe\\PhoneKeyAuthPipe");
            sb.AppendLine("Status: Registered as additive provider (Windows PIN/Hello preserved)");
            sb.AppendLine("=======================================================");

            TxtLog.Text = sb.ToString();
        }

        private void BtnCopy_Click(object sender, RoutedEventArgs e)
        {
            Clipboard.SetText(TxtLog.Text);
            MessageBox.Show("Diagnostics log copied to clipboard.", "Copied", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
