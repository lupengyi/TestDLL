using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace ManualCanDebug.Core
{
    public sealed class ProductLocatorDefinition
    {
        public ProductLocatorDefinition(string product, string sourcePath, IEnumerable<ProductLocatorTable> tables)
        {
            Product = product ?? string.Empty;
            SourcePath = sourcePath ?? string.Empty;
            Tables = tables.OrderBy(table => table.AddressOffset).ThenBy(table => table.Name, StringComparer.OrdinalIgnoreCase).ToList().AsReadOnly();
        }
        public string Product { get; private set; }
        public string SourcePath { get; private set; }
        public IReadOnlyList<ProductLocatorTable> Tables { get; private set; }
        public int SignalCount { get { return Tables.Sum(table => table.Signals.Count); } }
    }

    public sealed class ProductLocatorTable
    {
        public ProductLocatorTable(string name, uint addressOffset, int elementSize, string sheetName, IEnumerable<ProductLocatorSignal> signals)
        {
            Name = name ?? string.Empty;
            AddressOffset = addressOffset;
            ElementSize = elementSize <= 0 ? 1 : elementSize;
            SheetName = sheetName ?? string.Empty;
            Signals = signals.OrderBy(signal => signal.Offset).ThenBy(signal => signal.Name, StringComparer.OrdinalIgnoreCase).ToList().AsReadOnly();
            CanWrite = ProductLocatorParser.IsWritableTable(Name);
        }
        public string Name { get; private set; }
        public uint AddressOffset { get; private set; }
        public int ElementSize { get; private set; }
        public string SheetName { get; private set; }
        public IReadOnlyList<ProductLocatorSignal> Signals { get; private set; }
        public bool CanWrite { get; private set; }
        public string DisplayName { get { return string.Format(CultureInfo.InvariantCulture, "{0}  [0x{1:X2}]", Name, AddressOffset); } }
    }

    public sealed class ProductLocatorSignal
    {
        public ProductLocatorSignal(int offset, string name, string dataType, int dataSize, string unit, string comment)
        {
            Offset = offset;
            Name = name ?? string.Empty;
            DataType = string.IsNullOrWhiteSpace(dataType) ? "byte" : dataType.Trim();
            DataSize = dataSize <= 0 ? 1 : dataSize;
            Unit = unit ?? string.Empty;
            Comment = comment ?? string.Empty;
        }
        public int Offset { get; private set; }
        public string Name { get; private set; }
        public string DataType { get; private set; }
        public int DataSize { get; private set; }
        public string Unit { get; private set; }
        public string Comment { get; private set; }
        public string DisplayName { get { return string.Format(CultureInfo.InvariantCulture, "{0}  (+0x{1:X}, {2})", Name, Offset, DataType); } }
    }

    public static class ProductLocatorParser
    {
        private static readonly Regex SectionTitle = new Regex(@"(?<name>FT_[A-Za-z0-9_&\- ]+)\s*,\s*(?<offset>[0-9A-Fa-f]{1,4})", RegexOptions.Compiled);

        public static ProductLocatorDefinition Parse(string product, string path)
        {
            if (string.IsNullOrWhiteSpace(product)) throw new ArgumentException("Product name is required.", nameof(product));
            if (!File.Exists(path)) throw new FileNotFoundException("Locator workbook was not found.", path);
            List<WorksheetData> sheets = XlsxReader.Read(path);
            List<TableSeed> seeds = ReadAddressTable(sheets);
            if (seeds.Count == 0) AddSectionTitleSeeds(sheets, seeds);
            List<ProductLocatorTable> tables = new List<ProductLocatorTable>();
            foreach (TableSeed seed in seeds.GroupBy(item => Normalize(item.Name), StringComparer.OrdinalIgnoreCase).Select(group => group.First()))
            {
                TableLocation location = FindBestLocation(seed, sheets);
                if (location == null) continue;
                List<ProductLocatorSignal> signals = ReadSignals(seed, location);
                if (signals.Count == 0) continue;
                tables.Add(new ProductLocatorTable(seed.Name, seed.AddressOffset, seed.ElementSize, location.Sheet.Name, signals));
            }
            if (tables.Count == 0) throw new FormatException("No readable FT tables/signals were found in Locator: " + path);
            return new ProductLocatorDefinition(product.Trim().ToUpperInvariant(), path, tables);
        }

        public static int DataSizeForType(string dataType, int fallback)
        {
            string type = Normalize(dataType);
            if (type.Contains("double") || type.Contains("float64") || type.Contains("uint64") || type.Contains("int64")) return 8;
            if (type.Contains("float") || type.Contains("32") || type.Contains("uint32") || type.Contains("int32")) return 4;
            if (type.Contains("16") || type.Contains("short")) return 2;
            if (type.Contains("bool") || type.Contains("byte") || type.Contains("uint8") || type.Contains("int8") || type.Contains("char")) return 1;
            return fallback > 0 ? fallback : 1;
        }

        public static bool IsWritableTable(string tableName)
        {
            string value = Normalize(tableName);
            string[] writable = { "control", "cmd", "command", "output", "enable", "requested", "reset", "setting", "gain", "limit", "write", "autooutput", "motorcontrol" };
            return writable.Any(value.Contains);
        }

        private static List<TableSeed> ReadAddressTable(IEnumerable<WorksheetData> sheets)
        {
            List<TableSeed> result = new List<TableSeed>();
            foreach (WorksheetData sheet in sheets.Where(item => Normalize(item.Name).Contains("addresstable")))
            {
                foreach (int row in sheet.RowNumbers)
                {
                    Dictionary<int, string> values = sheet.Row(row);
                    int nameColumn = FindColumn(values, text => Normalize(text).Contains("tablename"));
                    int offsetColumn = FindColumn(values, text => Normalize(text) == "offset" || Normalize(text).Contains("tableoffset"));
                    if (nameColumn < 0 || offsetColumn < 0) continue;
                    int sizeColumn = FindColumn(values, text => Normalize(text).Contains("elementsize") || Normalize(text).Contains("datasize"));
                    int blank = 0;
                    for (int dataRow = row + 1; dataRow <= sheet.MaxRow; dataRow++)
                    {
                        string name = sheet.Value(dataRow, nameColumn).Trim();
                        string offsetText = sheet.Value(dataRow, offsetColumn).Trim();
                        if (name.Length == 0 || offsetText.Length == 0)
                        {
                            if (++blank >= 4) break;
                            continue;
                        }
                        blank = 0;
                        if (!name.StartsWith("FT_", StringComparison.OrdinalIgnoreCase)) continue;
                        uint address;
                        if (!TryParseNumber(offsetText, true, out address)) continue;
                        int elementSize = 1;
                        uint parsedSize;
                        if (sizeColumn >= 0 && TryParseNumber(sheet.Value(dataRow, sizeColumn), false, out parsedSize)) elementSize = (int)parsedSize;
                        result.Add(new TableSeed(name.Trim(), address, elementSize));
                    }
                    if (result.Count > 0) return result;
                }
            }
            return result;
        }

        private static void AddSectionTitleSeeds(IEnumerable<WorksheetData> sheets, ICollection<TableSeed> seeds)
        {
            HashSet<string> known = new HashSet<string>(seeds.Select(seed => Normalize(seed.Name)), StringComparer.OrdinalIgnoreCase);
            foreach (WorksheetData sheet in sheets)
            {
                foreach (CellData cell in sheet.Cells.Values)
                {
                    Match match = SectionTitle.Match(cell.Value ?? string.Empty);
                    if (!match.Success) continue;
                    string name = match.Groups["name"].Value.Trim().Replace(" ", string.Empty);
                    if (known.Contains(Normalize(name))) continue;
                    uint address;
                    if (!TryParseNumber(match.Groups["offset"].Value, true, out address)) continue;
                    seeds.Add(new TableSeed(name, address, 1));
                    known.Add(Normalize(name));
                }
            }
        }

        private static TableLocation FindBestLocation(TableSeed seed, IEnumerable<WorksheetData> sheets)
        {
            TableLocation best = null;
            int bestScore = 0;
            string target = Normalize(seed.Name).Replace("ft", string.Empty);
            foreach (WorksheetData sheet in sheets.Where(item => !Normalize(item.Name).Contains("addresstable"))) foreach (CellData cell in sheet.Cells.Values) { Match section = SectionTitle.Match(cell.Value ?? string.Empty); uint address; if (!section.Success || !TryParseNumber(section.Groups["offset"].Value, true, out address) || address != seed.AddressOffset) continue; HeaderLocation header = FindHeader(sheet, cell.Row, cell.Column); if (header == null) continue; string sectionName = Normalize(section.Groups["name"].Value); if (sectionName.StartsWith("ft", StringComparison.OrdinalIgnoreCase)) sectionName = sectionName.Substring(2); int score = MatchScore(target, sectionName); if (best == null || score > bestScore) { best = new TableLocation(sheet, cell.Row, cell.Column, header); bestScore = score; } }
            if (best != null) return best;
            foreach (WorksheetData sheet in sheets.Where(item => !Normalize(item.Name).Contains("addresstable")))
            {
                foreach (CellData cell in sheet.Cells.Values)
                {
                    string rawCandidate = (cell.Value ?? string.Empty).Split(',')[0]; string candidate = Normalize(rawCandidate); if (candidate.StartsWith("ft", StringComparison.OrdinalIgnoreCase)) candidate = candidate.Substring(2);
                    if (candidate.Length < 4) continue;
                    int score = MatchScore(target, candidate);
                    if (score <= bestScore) continue;
                    HeaderLocation header = FindHeader(sheet, cell.Row, cell.Column);
                    if (header == null) continue;
                    bestScore = score;
                    best = new TableLocation(sheet, cell.Row, cell.Column, header);
                }
            }
            if (bestScore >= 6) return best;
            foreach (WorksheetData sheet in sheets.Where(item => !Normalize(item.Name).Contains("addresstable"))) foreach (CellData cell in sheet.Cells.Values) { Match match = SectionTitle.Match(cell.Value ?? string.Empty); uint address; if (!match.Success || !TryParseNumber(match.Groups["offset"].Value, true, out address) || address != seed.AddressOffset) continue; HeaderLocation header = FindHeader(sheet, cell.Row, cell.Column); if (header != null) return new TableLocation(sheet, cell.Row, cell.Column, header); }
            return null;
        }

        private static HeaderLocation FindHeader(WorksheetData sheet, int anchorRow, int anchorColumn)
        {
            for (int row = anchorRow; row <= Math.Min(sheet.MaxRow, anchorRow + 5); row++)
            {
                Dictionary<int, string> values = sheet.Row(row);
                List<int> offsetColumns = values.Where(pair => pair.Key >= Math.Max(1, anchorColumn - 1) && pair.Key <= anchorColumn + 4 && Normalize(pair.Value).Contains("offset")).Select(pair => pair.Key).ToList();
                foreach (int offsetColumn in offsetColumns)
                {
                    int nameColumn = FindColumn(values, pair => pair.Key > offsetColumn && pair.Key <= offsetColumn + 5 && Normalize(pair.Value).Contains("name"));
                    int selectedOffset = offsetColumns.FirstOrDefault(column => Normalize(values[column]).Contains("dec"));
                    if (selectedOffset == 0) selectedOffset = offsetColumn;
                    string offsetHeader = values[selectedOffset];
                    int typeColumn = FindColumn(values, pair => pair.Key > selectedOffset && pair.Key <= selectedOffset + 7 && (Normalize(pair.Value).Contains("format") || Normalize(pair.Value) == "type" || Normalize(pair.Value).Contains("datatype") || Normalize(pair.Value).Contains("variabletype")));
                    int unitColumn = FindColumn(values, pair => pair.Key > selectedOffset && pair.Key <= selectedOffset + 8 && Normalize(pair.Value).Contains("unit"));
                    int commentColumn = FindColumn(values, pair => pair.Key > selectedOffset && pair.Key <= selectedOffset + 10 && (Normalize(pair.Value).Contains("comment") || Normalize(pair.Value).Contains("note")));
                    if (nameColumn < 0 && typeColumn >= 0 && commentColumn >= 0) { nameColumn = commentColumn; commentColumn = commentColumn + 1 <= sheet.Row(row + 1).Keys.DefaultIfEmpty(0).Max() ? commentColumn + 1 : -1; }
                    if (nameColumn < 0) continue;
                    return new HeaderLocation(row, selectedOffset, nameColumn, typeColumn, unitColumn, commentColumn, Normalize(offsetHeader).Contains("hex"), Normalize(offsetHeader).Contains("dec"));
                }
            }
            return null;
        }

        private static List<ProductLocatorSignal> ReadSignals(TableSeed seed, TableLocation location)
        {
            List<ProductLocatorSignal> result = new List<ProductLocatorSignal>();
            int blank = 0;
            for (int row = location.Header.Row + 1; row <= location.Sheet.MaxRow; row++)
            {
                string offsetText = location.Sheet.Value(row, location.Header.OffsetColumn).Trim();
                string name = location.Sheet.Value(row, location.Header.NameColumn).Trim();
                if (offsetText.Length == 0 || name.Length == 0)
                {
                    if (++blank >= 3) break;
                    continue;
                }
                blank = 0;
                uint offset;
                bool forceHex = location.Header.OffsetIsHex || (!location.Header.OffsetIsDecimal && offsetText.Any(character => "ABCDEFabcdef".Contains(character)));
                if (!TryParseNumber(offsetText, forceHex, out offset)) continue;
                if (name.Equals("Name", StringComparison.OrdinalIgnoreCase)) continue;
                string type = location.Header.TypeColumn < 0 ? string.Empty : location.Sheet.Value(row, location.Header.TypeColumn).Trim();
                string unit = location.Header.UnitColumn < 0 ? string.Empty : location.Sheet.Value(row, location.Header.UnitColumn).Trim();
                string comment = location.Header.CommentColumn < 0 ? string.Empty : location.Sheet.Value(row, location.Header.CommentColumn).Trim();
                if (string.IsNullOrWhiteSpace(type) || type == "-") type = InferType(name + " " + comment, seed.ElementSize);
                int size = DataSizeForType(type, seed.ElementSize);
                result.Add(new ProductLocatorSignal((int)offset, name, type, size, unit, comment));
            }
            return result.GroupBy(signal => signal.Offset.ToString(CultureInfo.InvariantCulture) + "|" + signal.Name, StringComparer.OrdinalIgnoreCase).Select(group => group.First()).ToList();
        }

        private static string InferType(string hint, int fallbackSize)
        {
            string value = Normalize(hint); if (value.Contains("double") || value.Contains("float64")) return "float64"; if (value.Contains("float")) return "float32"; if (value.Contains("uint64")) return "uint64"; if (value.Contains("int64")) return "int64"; if (value.Contains("uint32")) return "uint32"; if (value.Contains("int32")) return "int32"; if (value.Contains("uint16")) return "uint16"; if (value.Contains("int16")) return "int16"; if (value.Contains("uint8") || value.Contains("byte")) return "uint8"; return fallbackSize == 8 ? "float64" : fallbackSize == 4 ? "float32" : fallbackSize == 2 ? "uint16" : "uint8";
        }

        private static int MatchScore(string target, string candidate)
        {
            if (candidate.StartsWith(target, StringComparison.OrdinalIgnoreCase)) return target.Length + 20;
            if (target.StartsWith(candidate, StringComparison.OrdinalIgnoreCase)) return candidate.Length + 12;
            int common = 0;
            while (common < target.Length && common < candidate.Length && target[common] == candidate[common]) common++;
            if (target.Contains(candidate) || candidate.Contains(target)) common += 8;
            return common;
        }

        private static int FindColumn(Dictionary<int, string> values, Func<string, bool> predicate)
        {
            KeyValuePair<int, string> match = values.FirstOrDefault(pair => predicate(pair.Value));
            return match.Key == 0 ? -1 : match.Key;
        }

        private static int FindColumn(Dictionary<int, string> values, Func<KeyValuePair<int, string>, bool> predicate)
        {
            KeyValuePair<int, string> match = values.FirstOrDefault(predicate);
            return match.Key == 0 ? -1 : match.Key;
        }

        private static bool TryParseNumber(string text, bool hexadecimal, out uint value)
        {
            string candidate = (text ?? string.Empty).Trim();
            if (candidate.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) { candidate = candidate.Substring(2); hexadecimal = true; }
            if (hexadecimal) return uint.TryParse(candidate, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
            double real;
            if (double.TryParse(candidate, NumberStyles.Float, CultureInfo.InvariantCulture, out real) && real >= 0 && real <= uint.MaxValue) { value = (uint)real; return true; }
            value = 0; return false;
        }

        private static string Normalize(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            StringBuilder result = new StringBuilder();
            foreach (char character in value.ToLowerInvariant()) if (char.IsLetterOrDigit(character)) result.Append(character);
            return result.ToString();
        }

        private sealed class TableSeed
        {
            public TableSeed(string name, uint addressOffset, int elementSize) { Name = name; AddressOffset = addressOffset; ElementSize = elementSize; }
            public string Name; public uint AddressOffset; public int ElementSize;
        }
        private sealed class TableLocation
        {
            public TableLocation(WorksheetData sheet, int anchorRow, int anchorColumn, HeaderLocation header) { Sheet = sheet; AnchorRow = anchorRow; AnchorColumn = anchorColumn; Header = header; }
            public WorksheetData Sheet; public int AnchorRow; public int AnchorColumn; public HeaderLocation Header;
        }
        private sealed class HeaderLocation
        {
            public HeaderLocation(int row, int offsetColumn, int nameColumn, int typeColumn, int unitColumn, int commentColumn, bool offsetIsHex, bool offsetIsDecimal) { Row = row; OffsetColumn = offsetColumn; NameColumn = nameColumn; TypeColumn = typeColumn; UnitColumn = unitColumn; CommentColumn = commentColumn; OffsetIsHex = offsetIsHex; OffsetIsDecimal = offsetIsDecimal; }
            public int Row, OffsetColumn, NameColumn, TypeColumn, UnitColumn, CommentColumn; public bool OffsetIsHex, OffsetIsDecimal;
        }

        private sealed class CellData { public int Row, Column; public string Value; }
        private sealed class WorksheetData
        {
            public string Name;
            public Dictionary<string, CellData> Cells = new Dictionary<string, CellData>();
            public int MaxRow;
            public IEnumerable<int> RowNumbers { get { return Cells.Values.Select(cell => cell.Row).Distinct().OrderBy(value => value); } }
            public Dictionary<int, string> Row(int row) { return Cells.Values.Where(cell => cell.Row == row).ToDictionary(cell => cell.Column, cell => cell.Value ?? string.Empty); }
            public string Value(int row, int column) { CellData cell; return Cells.TryGetValue(row + ":" + column, out cell) ? cell.Value ?? string.Empty : string.Empty; }
        }

        private static class XlsxReader
        {
            private static readonly XNamespace Spreadsheet = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            private static readonly XNamespace OfficeRelationship = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
            private static readonly XNamespace PackageRelationship = "http://schemas.openxmlformats.org/package/2006/relationships";

            public static List<WorksheetData> Read(string path)
            {
                using (FileStream stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (ZipArchive archive = new ZipArchive(stream, ZipArchiveMode.Read))
                {
                    List<string> shared = ReadSharedStrings(archive);
                    XDocument workbook = LoadXml(archive, "xl/workbook.xml");
                    XDocument relationships = LoadXml(archive, "xl/_rels/workbook.xml.rels");
                    Dictionary<string, string> targets = relationships.Root.Elements(PackageRelationship + "Relationship").ToDictionary(element => (string)element.Attribute("Id"), element => (string)element.Attribute("Target"));
                    List<WorksheetData> result = new List<WorksheetData>();
                    foreach (XElement sheet in workbook.Descendants(Spreadsheet + "sheet"))
                    {
                        string id = (string)sheet.Attribute(OfficeRelationship + "id");
                        string target;
                        if (!targets.TryGetValue(id, out target)) continue;
                        string entryPath = target.StartsWith("/", StringComparison.Ordinal) ? target.TrimStart('/') : "xl/" + target.Replace("../", string.Empty);
                        ZipArchiveEntry entry = archive.GetEntry(entryPath);
                        if (entry == null) continue;
                        WorksheetData data = new WorksheetData { Name = (string)sheet.Attribute("name") ?? string.Empty };
                        XDocument document;
                        using (Stream sheetStream = entry.Open()) document = XDocument.Load(sheetStream);
                        foreach (XElement cell in document.Descendants(Spreadsheet + "c"))
                        {
                            string reference = (string)cell.Attribute("r") ?? string.Empty;
                            int row, column;
                            if (!ParseReference(reference, out row, out column)) continue;
                            string type = (string)cell.Attribute("t") ?? string.Empty;
                            string value = string.Empty;
                            if (type == "inlineStr") value = string.Concat(cell.Descendants(Spreadsheet + "t").Select(element => (string)element));
                            else
                            {
                                XElement raw = cell.Element(Spreadsheet + "v");
                                value = raw == null ? string.Empty : (string)raw ?? string.Empty;
                                int index;
                                if (type == "s" && int.TryParse(value, out index) && index >= 0 && index < shared.Count) value = shared[index];
                            }
                            if (string.IsNullOrWhiteSpace(value)) continue;
                            CellData parsed = new CellData { Row = row, Column = column, Value = value };
                            data.Cells[row + ":" + column] = parsed;
                            data.MaxRow = Math.Max(data.MaxRow, row);
                        }
                        result.Add(data);
                    }
                    return result;
                }
            }

            private static List<string> ReadSharedStrings(ZipArchive archive)
            {
                ZipArchiveEntry entry = archive.GetEntry("xl/sharedStrings.xml");
                if (entry == null) return new List<string>();
                XDocument document;
                using (Stream stream = entry.Open()) document = XDocument.Load(stream);
                return document.Descendants(Spreadsheet + "si").Select(item => string.Concat(item.Descendants(Spreadsheet + "t").Select(element => (string)element))).ToList();
            }

            private static XDocument LoadXml(ZipArchive archive, string name)
            {
                ZipArchiveEntry entry = archive.GetEntry(name);
                if (entry == null) throw new FormatException("Missing XLSX part: " + name);
                using (Stream stream = entry.Open()) return XDocument.Load(stream);
            }

            private static bool ParseReference(string reference, out int row, out int column)
            {
                Match match = Regex.Match(reference ?? string.Empty, @"^(?<column>[A-Z]+)(?<row>[0-9]+)$");
                if (!match.Success) { row = 0; column = 0; return false; }
                row = int.Parse(match.Groups["row"].Value, CultureInfo.InvariantCulture);
                column = 0;
                foreach (char character in match.Groups["column"].Value) column = column * 26 + character - 'A' + 1;
                return true;
            }
        }
    }
}
