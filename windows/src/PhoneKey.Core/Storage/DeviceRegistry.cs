using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using PhoneKey.Core.Crypto;
using PhoneKey.Core.Protocol;

namespace PhoneKey.Core.Storage
{
    public sealed class EnrolledDevice
    {
        public Guid DeviceId { get; set; } = Guid.NewGuid();
        public string DeviceName { get; set; } = "Android Phone";
        public string PublicKeyBase64 { get; set; } = string.Empty;
        public string SharedSecretBase64 { get; set; } = string.Empty; // 32 bytes derived during pairing
        public HardwareSecurityLevel HardwareLevel { get; set; } = HardwareSecurityLevel.Tee;
        public DateTimeOffset EnrolledAtUtc { get; set; } = DateTimeOffset.UtcNow;
        public DateTimeOffset LastAuthenticatedUtc { get; set; } = DateTimeOffset.MinValue;
        public bool IsRevoked { get; set; } = false;

        public byte[] GetPublicKeyBytes() => Convert.FromBase64String(PublicKeyBase64);
        public byte[] GetSharedSecretBytes() => Convert.FromBase64String(SharedSecretBase64);
    }

    public sealed class DeviceRegistry
    {
        private static readonly byte[] EntropySalt = "PhoneKey-Registry-Entropy-v1"u8.ToArray();
        private readonly string _storagePath;
        private readonly object _lock = new();

        public DeviceRegistry(string? customPath = null)
        {
            if (customPath != null)
            {
                _storagePath = customPath;
            }
            else
            {
                string programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
                string dir = Path.Combine(programData, "PhoneKey");
                Directory.CreateDirectory(dir);
                _storagePath = Path.Combine(dir, "devices.dat");
            }
        }

        public List<EnrolledDevice> GetDevices()
        {
            lock (_lock)
            {
                if (!File.Exists(_storagePath)) return new List<EnrolledDevice>();

                try
                {
                    byte[] encrypted = File.ReadAllBytes(_storagePath);
                    byte[] decrypted = ProtectedData.Unprotect(encrypted, EntropySalt, DataProtectionScope.LocalMachine);
                    string json = System.Text.Encoding.UTF8.GetString(decrypted);
                    return JsonSerializer.Deserialize<List<EnrolledDevice>>(json) ?? new List<EnrolledDevice>();
                }
                catch
                {
                    return new List<EnrolledDevice>();
                }
            }
        }

        public void SaveDevice(EnrolledDevice device)
        {
            lock (_lock)
            {
                var list = GetDevices();
                int idx = list.FindIndex(d => d.DeviceId == device.DeviceId);
                if (idx >= 0)
                {
                    list[idx] = device;
                }
                else
                {
                    list.Add(device);
                }
                SaveListInternal(list);
            }
        }

        public bool RevokeDevice(Guid deviceId)
        {
            lock (_lock)
            {
                var list = GetDevices();
                var found = list.Find(d => d.DeviceId == deviceId);
                if (found != null)
                {
                    found.IsRevoked = true;
                    SaveListInternal(list);
                    return true;
                }
                return false;
            }
        }

        public bool DeleteDevice(Guid deviceId)
        {
            lock (_lock)
            {
                var list = GetDevices();
                int removed = list.RemoveAll(d => d.DeviceId == deviceId);
                if (removed > 0)
                {
                    SaveListInternal(list);
                    return true;
                }
                return false;
            }
        }

        public void ClearAll()
        {
            lock (_lock)
            {
                if (File.Exists(_storagePath))
                {
                    File.Delete(_storagePath);
                }
            }
        }

        private void SaveListInternal(List<EnrolledDevice> list)
        {
            string json = JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true });
            byte[] plaintext = System.Text.Encoding.UTF8.GetBytes(json);
            byte[] encrypted = ProtectedData.Protect(plaintext, EntropySalt, DataProtectionScope.LocalMachine);
            File.WriteAllBytes(_storagePath, encrypted);
        }
    }
}
