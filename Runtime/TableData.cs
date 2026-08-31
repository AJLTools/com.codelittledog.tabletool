using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace CodeLittleDog.TableTool
{
    /// <summary>一行数据：字段名 -> 单值字符串。</summary>
    public class TableRow
    {
        private readonly Dictionary<string, string> _values;
        private readonly Dictionary<string, Type> _types;

        public TableRow(Dictionary<string, string> values, Dictionary<string, Type> types)
        {
            _values = values ?? new Dictionary<string, string>();
            _types = types ?? new Dictionary<string, Type>();
        }

        public string GetValue(string fieldName) => _values.TryGetValue(fieldName, out var v) ? v : null;
        public bool HasField(string fieldName) => _values.ContainsKey(fieldName);
        public IEnumerable<string> FieldNames => _values.Keys;

        public T GetValue<T>(string fieldName)
        {
            string raw = GetValue(fieldName);
            Type t = typeof(T);
            var v = TableTypeConverter.Convert(raw, t, out var err);
            if (err != null) Debug.LogWarning($"[TableData] {err}");
            return v == null ? default : (T)v;
        }
    }

    public struct TableValidationError
    {
        public int RowIndex;     // -1 表示表头行；否则为数据行索引(0 起)
        public string Field;
        public string Value;
        public string ExpectedType;
        public string Message;

        public override string ToString()
        {
            string loc = RowIndex < 0 ? "表头" : ("第" + (RowIndex + 2) + "行"); // +2: 表头占第1行
            return $"[{loc}] 字段={Field} 值='{Value}' 期望={ExpectedType} -> {Message}";
        }
    }

    public class TableValidationResult
    {
        public bool IsValid => Errors.Count == 0;
        public readonly List<TableValidationError> Errors = new List<TableValidationError>();
        public readonly List<string> Warnings = new List<string>();

        public void AddError(int row, string field, string value, string type, string msg)
            => Errors.Add(new TableValidationError { RowIndex = row, Field = field, Value = value, ExpectedType = type, Message = msg });

        public void Log()
        {
            foreach (var e in Errors) Debug.LogError($"[Table校验失败] {e}");
            foreach (var w in Warnings) Debug.LogWarning($"[Table校验警告] {w}");
            if (IsValid) Debug.Log("[Table校验] 通过，无错误。");
        }
    }

    /// <summary>严格模式下抛出。</summary>
    public class TableValidationException : Exception
    {
        public readonly TableValidationResult Result;
        public TableValidationException(TableValidationResult result)
            : base("表格校验失败，共 " + result.Errors.Count + " 处错误。第一条: " + (result.Errors.Count > 0 ? result.Errors[0].ToString() : ""))
        { Result = result; }
    }

    /// <summary>
    /// 表格数据：表名、字段定义、全部行、O(1) 查找索引。解析时自动校验。
    /// </summary>
    public class TableData
    {
        public string TableName { get; private set; }
        public string SheetName { get; private set; }
        public List<string> FieldNames { get; private set; }
        public List<Type> FieldTypes { get; private set; }
        public List<TableRow> Rows { get; private set; }
        public TableValidationResult Validation { get; private set; }

        private readonly Dictionary<string, Dictionary<string, List<TableRow>>> _indexes
            = new Dictionary<string, Dictionary<string, List<TableRow>>>();

        private TableData() { }

        public static TableData Parse(TableRaw raw, bool strict = false)
        {
            var data = new TableData
            {
                TableName = raw.TableName,
                SheetName = raw.SheetName,
                FieldNames = new List<string>(),
                FieldTypes = new List<Type>(),
                Rows = new List<TableRow>(),
                Validation = new TableValidationResult()
            };

            if (raw.Headers == null || raw.Headers.Count == 0)
            {
                data.Validation.AddError(-1, "", "", "", "表头为空");
                if (strict) throw new TableValidationException(data.Validation);
                return data;
            }

            var colTypes = new Dictionary<string, Type>();
            for (int i = 0; i < raw.Headers.Count; i++)
            {
                string h = raw.Headers[i];
                var (fname, ftype, ok, msg) = ParseFieldHeader(h);
                if (!ok)
                {
                    data.Validation.AddError(-1, h, "", "", msg);
                    fname = string.IsNullOrEmpty(fname) ? $"col{i}" : fname;
                    ftype = typeof(string);
                }
                data.FieldNames.Add(fname);
                data.FieldTypes.Add(ftype);
                colTypes[fname] = ftype;
            }

            for (int r = 0; r < raw.Rows.Count; r++)
            {
                var rawRow = raw.Rows[r];
                var values = new Dictionary<string, string>();
                for (int c = 0; c < data.FieldNames.Count; c++)
                {
                    string fname = data.FieldNames[c];
                    Type ftype = data.FieldTypes[c];
                    string cell = c < rawRow.Count ? rawRow[c] : "";
                    string trimmed = cell == null ? "" : cell.Trim();

                    if (!TableTypeConverter.TryConvert(trimmed, ftype, out _, out var err) && err != null)
                        data.Validation.AddError(r, fname, cell, TableTypeConverter.CSharpName(ftype), err);
                    values[fname] = trimmed;
                }
                data.Rows.Add(new TableRow(values, colTypes));
            }

            if (strict && !data.Validation.IsValid)
                throw new TableValidationException(data.Validation);

            return data;
        }

        /// <summary>从文件路径加载(.xlsx/.csv/.tsv/.txt 自动识别)。strict=true 时校验失败抛异常。</summary>
        public static TableData Load(string filePath, bool strict = false, string sheetName = null)
        {
            var raw = TableSource.Load(filePath, sheetName);
            if (raw == null) return null;
            return Parse(raw, strict);
        }

        public static TableData LoadFromString(string text, string tableName, bool strict = false)
        {
            var raw = TextTableSource.Parse(text, tableName);
            return Parse(raw, strict);
        }

        public static TableData Load(TextAsset asset, bool strict = false)
        {
            if (asset == null) return null;
            return LoadFromString(asset.text, asset.name, strict);
        }

        /// <summary>从 StreamingAssets 异步加载(全平台：WebGL/Android APK 内/iOS/桌面均可用)。
        /// relPath 为相对 StreamingAssets 的路径(如 "Tables/PlayerConfig.csv")。
        /// 用法：yield return TableData.LoadFromStreamingAssets("Tables/x.csv", data => { ... });
        /// 或用 MonoBehaviour.StartCoroutine(TableData.LoadFromStreamingAssets(...));</summary>
        public static IEnumerator LoadFromStreamingAssets(string relPath, Action<TableData> onComplete, bool strict = false, string sheetName = null)
        {
            yield return TableWebLoader.LoadStreamingAssetTextCoroutine(relPath, text =>
            {
                if (string.IsNullOrEmpty(text)) { onComplete?.Invoke(null); return; }
                // csv/tsv 直接解析；xlsx 在 WebGL/iOS 无法解析
                string name = Path.GetFileNameWithoutExtension(relPath);
                onComplete?.Invoke(LoadFromString(text, name, strict));
            });
        }

        public static (string name, Type type, bool ok, string error) ParseFieldHeader(string headerCell)
        {
            headerCell = (headerCell ?? "").Trim();
            if (headerCell.Length == 0) return ("", typeof(string), false, "表头单元格为空");
            int lp = headerCell.IndexOf('(');
            int rp = headerCell.LastIndexOf(')');
            if (lp <= 0 || rp <= lp)
                return (headerCell, typeof(string), false, $"表头 '{headerCell}' 缺少 (类型) 声明");
            string fname = headerCell.Substring(0, lp).Trim();
            string typeName = headerCell.Substring(lp + 1, rp - lp - 1).Trim();
            if (fname.Length == 0) return ("", typeof(string), false, $"表头 '{headerCell}' 字段名为空");
            Type t = TableTypeConverter.ResolveType(typeName);
            bool known = typeName.ToLowerInvariant() switch
            {
                "string" or "str" or "int" or "i32" or "long" or "i64" or
                "float" or "f32" or "double" or "f64" or "bool" or "boolean" => true,
                _ => false
            };
            if (!known)
                return (fname, t, false, $"表头 '{headerCell}' 类型 '{typeName}' 未知，已按 string 处理");
            return (fname, t, true, null);
        }

        public void BuildIndex(string fieldName)
        {
            if (_indexes.ContainsKey(fieldName)) return;
            var idx = new Dictionary<string, List<TableRow>>();
            foreach (var row in Rows)
            {
                string v = row.GetValue(fieldName);
                if (string.IsNullOrEmpty(v)) continue;
                if (!idx.TryGetValue(v, out var bucket))
                {
                    bucket = new List<TableRow>();
                    idx[v] = bucket;
                }
                bucket.Add(row);
            }
            _indexes[fieldName] = idx;
        }

        public List<TableRow> FindBy(string fieldName, string value)
        {
            if (value == null) return new List<TableRow>();
            BuildIndex(fieldName);
            return _indexes[fieldName].TryGetValue(value, out var b) ? b : new List<TableRow>();
        }

        public TableRow FindFirst(string fieldName, string value)
        {
            var list = FindBy(fieldName, value);
            return list.Count > 0 ? list[0] : null;
        }

        public string ToText()
        {
            var sb = new StringBuilder();
            for (int i = 0; i < FieldNames.Count; i++)
            {
                if (i > 0) sb.Append('\t');
                sb.Append(FieldNames[i]).Append('(').Append(TableTypeConverter.TypeName(FieldTypes[i])).Append(')');
            }
            sb.AppendLine();
            foreach (var row in Rows)
            {
                for (int i = 0; i < FieldNames.Count; i++)
                {
                    if (i > 0) sb.Append('\t');
                    sb.Append(row.GetValue(FieldNames[i]) ?? "");
                }
                sb.AppendLine();
            }
            return sb.ToString();
        }

        public void Save(string filePath)
        {
            string dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(filePath, ToText(), Encoding.UTF8);
        }
    }
}
