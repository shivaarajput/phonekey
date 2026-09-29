using System;
using System.IO;

namespace PhoneKey.Core.Storage
{
    public static class PhoneKeyEnvironment
    {
        public static string CommonDataPath =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "PhoneKey");

        public static Guid GetOrCreatePcId()
        {
            string dir = CommonDataPath;
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, "pcid.dat");

            if (File.Exists(path) && Guid.TryParse(File.ReadAllText(path).Trim(), out var existing))
            {
                return existing;
            }

            var newId = Guid.NewGuid();
            File.WriteAllText(path, newId.ToString());
            return newId;
        }
    }
}
