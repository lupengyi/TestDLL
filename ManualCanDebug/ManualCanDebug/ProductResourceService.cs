using System;
using System.IO;

namespace ManualCanDebug
{
    internal static class ProductResourceService
    {
        public static string ImportDbc(string baseDirectory, string product, string sourcePath)
        {
            if (string.IsNullOrWhiteSpace(sourcePath)) return string.Empty;
            if (!File.Exists(sourcePath)) throw new FileNotFoundException("DBC文件不存在。", sourcePath);
            string directory = Path.Combine(baseDirectory, "Config", "ProductDbcs"); Directory.CreateDirectory(directory);
            string destination = Path.Combine(directory, product.ToUpperInvariant() + "_Auxiliary.dbc"); File.Copy(sourcePath, destination, true);
            return "Config\\ProductDbcs\\" + Path.GetFileName(destination);
        }
    }
}
