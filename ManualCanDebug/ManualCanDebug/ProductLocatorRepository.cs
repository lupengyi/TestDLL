using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using ManualCanDebug.Core;

namespace ManualCanDebug
{
    internal sealed class ProductLocatorRepository
    {
        private readonly string _directory;
        private readonly Action<string> _log;
        private readonly Dictionary<string, ProductLocatorDefinition> _products = new Dictionary<string, ProductLocatorDefinition>(StringComparer.OrdinalIgnoreCase);

        public ProductLocatorRepository(string baseDirectory, Action<string> log)
        {
            _directory = Path.Combine(baseDirectory, "Config", "ProductLocators");
            _log = log ?? delegate { };
            Directory.CreateDirectory(_directory);
            Reload();
        }

        public IReadOnlyList<ProductLocatorDefinition> Products { get { return _products.Values.OrderBy(product => product.Product, StringComparer.OrdinalIgnoreCase).ToList().AsReadOnly(); } }

        public void Reload()
        {
            _products.Clear();
            foreach (string file in Directory.GetFiles(_directory, "*.xlsx").OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                Match match = Regex.Match(Path.GetFileName(file), @"C\d+", RegexOptions.IgnoreCase);
                if (!match.Success) continue;
                string product = match.Value.ToUpperInvariant();
                try
                {
                    ProductLocatorDefinition parsed = ProductLocatorParser.Parse(product, file);
                    _products[product] = parsed;
                    _log(string.Format("Locator已解析：{0}，{1}张表，{2}个信号。", product, parsed.Tables.Count, parsed.SignalCount));
                }
                catch (Exception ex)
                {
                    _log("Locator解析失败：" + file + "；" + ex.Message);
                }
            }
            ProductLocatorDefinition c96;
            if (!_products.ContainsKey("C92") && _products.TryGetValue("C96", out c96))
                _products["C92"] = new ProductLocatorDefinition("C92", c96.SourcePath, c96.Tables);
        }

        public ProductLocatorDefinition Import(string product, string sourcePath)
        {
            string normalized = (product ?? string.Empty).Trim().ToUpperInvariant();
            if (!Regex.IsMatch(normalized, @"^C\d+$")) throw new FormatException("产品型号必须是C加数字，例如C97。");
            ProductLocatorDefinition verified = ProductLocatorParser.Parse(normalized, sourcePath);
            string destination = Path.Combine(_directory, normalized + "_Locator.xlsx");
            File.Copy(sourcePath, destination, true);
            ProductLocatorDefinition stored = ProductLocatorParser.Parse(normalized, destination);
            _products[normalized] = stored;
            _log(string.Format("新产品Locator已导入：{0}，{1}张表，{2}个信号。", normalized, stored.Tables.Count, stored.SignalCount));
            return stored;
        }
    }
}
