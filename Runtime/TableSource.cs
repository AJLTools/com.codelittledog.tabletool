using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace CodeLittleDog.TableTool
{
    /// <summary>
    /// 原始表格数据：首行=表头(形如 "name(string)")，其余=数据行。
    /// 各 Source 负责把文件解析成 TableRaw，TableData 再做字段解析/校验/索引。
    /// </summary>
    public class TableRaw
    {
        public string TableName;
        public string SheetName;
        public List<string> Headers = new List<string>();
        public List<List<string>> Rows = new List<List<string>>();
    }

    /// <summary>表格数据来源抽象。不同格式实现该接口。</summary>
    public interface ITableSource
    {
        TableRaw Load(string filePath, string sheetName = null);
    }

    /// <summary>
    /// 按扩展名自动选择 Source。
    /// 平台支持：
    ///   - .xlsx 仅在 Editor / Standalone / Android 编译(支持原生解析，零依赖)；
    ///   - .csv/.tsv/.txt 全平台支持。
    /// WebGL 不支持 xlsx(System.IO.Compression 不可用)，请用 csv/tsv。
    /// </summary>
    public static class TableSource
    {
        public static TableRaw Load(string filePath, string sheetName = null)
        {
            if (!File.Exists(filePath))
            {
                Debug.LogError($"[TableSource] 文件不存在: {filePath}");
                return null;
            }
            string ext = Path.GetExtension(filePath).ToLowerInvariant();
            ITableSource source;
            if (ext == ".xlsx")
            {
#if !UNITY_WEBGL && !UNITY_IOS && !UNITY_TVOS
                source = new ExcelXlsxSource();
#else
                Debug.LogError("[TableSource] 当前平台(WebGL/iOS/TVOS)不支持 xlsx 解析，请改用 csv/tsv/txt，或在 Editor 用菜单 Tools > Table CSV Exporter 导出 csv。");
                return null;
#endif
            }
            else if (ext == ".csv" || ext == ".tsv" || ext == ".txt")
            {
                source = new TextTableSource();
            }
            else
            {
                Debug.LogError($"[TableSource] 不支持的扩展名 {ext}，仅支持 .xlsx/.csv/.tsv/.txt");
                return null;
            }
            TableRaw raw = source.Load(filePath, sheetName);
            if (raw != null && string.IsNullOrEmpty(raw.TableName))
                raw.TableName = Path.GetFileNameWithoutExtension(filePath);
            return raw;
        }
    }

    /// <summary>
    /// 文本表格读取：Tab 或逗号分隔。CSV 支持引号包裹(含逗号/换行/转义双引号)。全平台可用。
    /// </summary>
    public class TextTableSource : ITableSource
    {
        public TableRaw Load(string filePath, string sheetName = null)
        {
            string text = File.ReadAllText(filePath);
            string name = Path.GetFileNameWithoutExtension(filePath);
            return Parse(text, name);
        }

        public static TableRaw Parse(string text, string tableName)
        {
            var raw = new TableRaw { TableName = tableName };
            if (string.IsNullOrEmpty(text)) return raw;

            char sep = text.IndexOf('\t') >= 0 ? '\t' : ',';
            var rows = SplitCsv(text, sep);
            if (rows.Count == 0) return raw;

            raw.Headers = rows[0];
            for (int i = 1; i < rows.Count; i++) raw.Rows.Add(rows[i]);
            return raw;
        }

        private static List<List<string>> SplitCsv(string text, char sep)
        {
            var result = new List<List<string>>();
            var cur = new List<string>();
            var sb = new System.Text.StringBuilder();
            bool inQuotes = false;
            int i = 0;
            while (i < text.Length)
            {
                char c = text[i];
                if (inQuotes)
                {
                    if (c == '"')
                    {
                        if (i + 1 < text.Length && text[i + 1] == '"') { sb.Append('"'); i += 2; continue; }
                        inQuotes = false; i++; continue;
                    }
                    sb.Append(c); i++; continue;
                }
                if (c == '"') { inQuotes = true; i++; continue; }
                if (c == sep) { cur.Add(sb.ToString()); sb.Clear(); i++; continue; }
                if (c == '\r')
                {
                    if (i + 1 < text.Length && text[i + 1] == '\n') i++;
                    cur.Add(sb.ToString()); sb.Clear();
                    if (cur.Count > 0 && !(cur.Count == 1 && cur[0].Length == 0)) result.Add(cur);
                    cur = new List<string>(); i++; continue;
                }
                if (c == '\n')
                {
                    cur.Add(sb.ToString()); sb.Clear();
                    if (cur.Count > 0 && !(cur.Count == 1 && cur[0].Length == 0)) result.Add(cur);
                    cur = new List<string>(); i++; continue;
                }
                sb.Append(c); i++;
            }
            if (sb.Length > 0 || cur.Count > 0)
            {
                cur.Add(sb.ToString());
                if (!(cur.Count == 1 && cur[0].Length == 0)) result.Add(cur);
            }
            return result;
        }
    }

#if !UNITY_WEBGL && !UNITY_IOS && !UNITY_TVOS
    /// <summary>
    /// 原生 .xlsx 读取：用 System.IO.Compression 解析 zip + Xml，**零第三方依赖**。
    /// 仅 Editor / Standalone / Android 编译。WebGL/iOS 不支持(System.IO.Compression 在 IL2CPP 下不可用)。
    /// 支持：多 Sheet(按名选择，默认第一个)、共享字符串、内联字符串、数字、布尔、公式缓存值。
    /// 不支持：.xls 旧二进制格式、合并单元格(取左上格)。
    /// </summary>
    public class ExcelXlsxSource : ITableSource
    {
        public TableRaw Load(string filePath, string sheetName = null)
        {
            try
            {
                using (var fs = File.OpenRead(filePath))
                using (var zip = new System.IO.Compression.ZipArchive(fs, System.IO.Compression.ZipArchiveMode.Read))
                {
                    string[] sharedStrings = ReadSharedStrings(zip);
                    var sheetTarget = ResolveSheetTarget(zip, sheetName, out string actualSheetName);
                    if (sheetTarget == null)
                    {
                        Debug.LogError($"[ExcelXlsxSource] 未找到 Sheet '{sheetName}'");
                        return null;
                    }
                    var raw = ReadSheet(zip, sheetTarget, sharedStrings);
                    raw.TableName = Path.GetFileNameWithoutExtension(filePath);
                    raw.SheetName = actualSheetName;
                    return raw;
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[ExcelXlsxSource] 读取 {filePath} 失败: {e.Message}");
                return null;
            }
        }

        private static System.IO.Compression.ZipArchiveEntry Entry(System.IO.Compression.ZipArchive zip, string path)
        {
            string p = path.Replace('\\', '/');
            foreach (var e in zip.Entries)
                if (e.FullName.Replace('\\', '/') == p) return e;
            return null;
        }

        private static string[] ReadSharedStrings(System.IO.Compression.ZipArchive zip)
        {
            var entry = Entry(zip, "xl/sharedStrings.xml");
            if (entry == null) return Array.Empty<string>();
            var list = new List<string>();
            using (var s = entry.Open())
            using (var reader = System.Xml.XmlReader.Create(s))
            {
                while (reader.Read())
                {
                    if (reader.NodeType == System.Xml.XmlNodeType.Element && reader.LocalName == "si")
                        list.Add(ReadSharedStringItem(reader));
                }
            }
            return list.ToArray();
        }

        private static string ReadSharedStringItem(System.Xml.XmlReader reader)
        {
            var sb = new System.Text.StringBuilder();
            int depth = reader.Depth;
            while (reader.Read())
            {
                if (reader.NodeType == System.Xml.XmlNodeType.EndElement && reader.LocalName == "si" && reader.Depth == depth) break;
                if (reader.NodeType == System.Xml.XmlNodeType.Element && reader.LocalName == "t")
                {
                    if (reader.IsEmptyElement) continue;
                    if (reader.Read() && (reader.NodeType == System.Xml.XmlNodeType.Text || reader.NodeType == System.Xml.XmlNodeType.CDATA
                        || reader.NodeType == System.Xml.XmlNodeType.Whitespace || reader.NodeType == System.Xml.XmlNodeType.SignificantWhitespace))
                        sb.Append(reader.Value);
                }
            }
            return sb.ToString();
        }

        private static string ResolveSheetTarget(System.IO.Compression.ZipArchive zip, string sheetName, out string actualSheetName)
        {
            actualSheetName = null;
            var entry = Entry(zip, "xl/workbook.xml");
            if (entry == null) return "xl/worksheets/sheet1.xml";

            var sheets = new List<(string name, string rid)>();
            using (var s = entry.Open())
            using (var reader = System.Xml.XmlReader.Create(s))
            {
                while (reader.Read())
                {
                    if (reader.NodeType == System.Xml.XmlNodeType.Element && reader.LocalName == "sheet")
                    {
                        string name = reader.GetAttribute("name");
                        string rid = null;
                        for (int i = 0; i < reader.AttributeCount; i++)
                        {
                            reader.MoveToAttribute(i);
                            if (reader.LocalName == "id" && reader.NamespaceURI.EndsWith("/relationships"))
                            { rid = reader.Value; }
                        }
                        sheets.Add((name, rid));
                    }
                }
            }
            if (sheets.Count == 0) return "xl/worksheets/sheet1.xml";

            int idx;
            if (!string.IsNullOrEmpty(sheetName))
            {
                idx = sheets.FindIndex(x => x.name == sheetName);
                if (idx < 0) return null;
            }
            else idx = 0;
            actualSheetName = sheets[idx].name;
            string targetRid = sheets[idx].rid;

            var rels = Entry(zip, "xl/_rels/workbook.xml.rels");
            if (rels == null) return "xl/worksheets/sheet1.xml";
            using (var s = rels.Open())
            using (var reader = System.Xml.XmlReader.Create(s))
            {
                while (reader.Read())
                {
                    if (reader.NodeType == System.Xml.XmlNodeType.Element && reader.LocalName == "Relationship")
                    {
                        string id = reader.GetAttribute("Id");
                        string target = reader.GetAttribute("Target");
                        if (id == targetRid)
                        {
                            if (target.StartsWith("/")) target = target.Substring(1);
                            else target = "xl/" + target;
                            return target.Replace('\\', '/');
                        }
                    }
                }
            }
            return "xl/worksheets/sheet1.xml";
        }

        private static TableRaw ReadSheet(System.IO.Compression.ZipArchive zip, string sheetPath, string[] sharedStrings)
        {
            var raw = new TableRaw();
            var entry = Entry(zip, sheetPath);
            if (entry == null) return raw;

            using (var s = entry.Open())
            using (var reader = System.Xml.XmlReader.Create(s))
            {
                int maxCol = -1;
                var rowList = new List<Dictionary<int, string>>();

                while (reader.Read())
                {
                    if (reader.NodeType == System.Xml.XmlNodeType.Element && reader.LocalName == "row")
                    {
                        var cells = new Dictionary<int, string>();
                        int depth = reader.Depth;
                        bool isEmpty = reader.IsEmptyElement;
                        if (!isEmpty)
                        {
                            while (reader.Read())
                            {
                                if (reader.NodeType == System.Xml.XmlNodeType.EndElement && reader.LocalName == "row" && reader.Depth == depth) break;
                                if (reader.NodeType == System.Xml.XmlNodeType.Element && reader.LocalName == "c")
                                {
                                    string r = reader.GetAttribute("r");
                                    string t = reader.GetAttribute("t");
                                    int col = ParseColumn(r);
                                    string val = ReadCellValue(reader, t, sharedStrings);
                                    if (col >= 0)
                                    {
                                        cells[col] = val;
                                        if (col > maxCol) maxCol = col;
                                    }
                                }
                            }
                        }
                        rowList.Add(cells);
                    }
                }

                for (int ri = 0; ri < rowList.Count; ri++)
                {
                    var cells = rowList[ri];
                    var row = new List<string>(maxCol + 1);
                    for (int c = 0; c <= maxCol; c++)
                        row.Add(cells.TryGetValue(c, out var v) ? v : "");
                    if (ri == 0) raw.Headers = row;
                    else raw.Rows.Add(row);
                }
            }
            return raw;
        }

        private static string ReadCellValue(System.Xml.XmlReader reader, string t, string[] sharedStrings)
        {
            int depth = reader.Depth;
            if (reader.IsEmptyElement) return "";
            string value = null;
            string inlineText = null;
            while (reader.Read())
            {
                if (reader.NodeType == System.Xml.XmlNodeType.EndElement && reader.LocalName == "c" && reader.Depth == depth) break;
                if (reader.NodeType == System.Xml.XmlNodeType.Element && reader.LocalName == "v")
                {
                    if (!reader.IsEmptyElement && reader.Read() && (reader.NodeType == System.Xml.XmlNodeType.Text || reader.NodeType == System.Xml.XmlNodeType.CDATA))
                        value = reader.Value;
                }
                else if (reader.NodeType == System.Xml.XmlNodeType.Element && reader.LocalName == "t")
                {
                    if (!reader.IsEmptyElement && reader.Read() && (reader.NodeType == System.Xml.XmlNodeType.Text || reader.NodeType == System.Xml.XmlNodeType.CDATA))
                        inlineText = reader.Value;
                }
            }

            if (t == "s")
            {
                if (int.TryParse(value, out int idx) && idx >= 0 && idx < sharedStrings.Length) return sharedStrings[idx];
                return "";
            }
            if (t == "inlineStr") return inlineText ?? "";
            if (t == "b") return value == "1" ? "true" : "false";
            return value ?? "";
        }

        private static int ParseColumn(string reference)
        {
            if (string.IsNullOrEmpty(reference)) return -1;
            int col = 0;
            for (int i = 0; i < reference.Length; i++)
            {
                char c = reference[i];
                if (c >= 'A' && c <= 'Z') col = col * 26 + (c - 'A' + 1);
                else if (c >= 'a' && c <= 'z') col = col * 26 + (c - 'a' + 1);
                else break;
            }
            return col - 1;
        }
    }
#endif
}
