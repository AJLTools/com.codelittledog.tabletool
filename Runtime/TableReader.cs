using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Networking;

namespace CodeLittleDog.TableTool
{
    /// <summary>
    /// 泛型表格读取器。T 为「先用模板表生成的公共类」。
    /// 加载时：1) 解析文件(.xlsx/.csv/.tsv/.txt) 2) 校验数据格式 3) 校验 T 字段与表格列一致 4) 反射填充 5) 为每字段建 O(1) 索引。
    /// 同路径默认走静态缓存(只解析一次)。
    /// </summary>
    public class TableReader<T> where T : class, new()
    {
        private readonly TableData _data;
        private readonly List<T> _items;
        private readonly Dictionary<string, FieldMap> _fieldMaps = new Dictionary<string, FieldMap>();
        private readonly Dictionary<string, Dictionary<object, List<T>>> _indexes
            = new Dictionary<string, Dictionary<object, List<T>>>(FieldComparer.Instance);

        public int Count => _items.Count;
        public string TableName => _data.TableName;
        public List<T> Items => _items;
        public List<string> FieldNames => _data.FieldNames;
        public TableValidationResult Validation => _data.Validation;

        private static readonly Dictionary<string, TableReader<T>> _cache
            = new Dictionary<string, TableReader<T>>(StringComparer.OrdinalIgnoreCase);

        private struct FieldMap
        {
            public FieldInfo Field;
            public Type FieldType;
            public string ColumnName;
        }

        private TableReader(TableData data)
        {
            _data = data;
            _items = new List<T>(data.Rows.Count);
        }

        /// <summary>从文件加载(.xlsx/.csv/.tsv/.txt)。默认走缓存、非严格模式。</summary>
        public static TableReader<T> Load(string filePath) => Load(filePath, TableLoadOptions.Default);

        /// <summary>带自定义选项加载。</summary>
        public static TableReader<T> Load(string filePath, TableLoadOptions options)
        {
            if (options.UseCache && !string.IsNullOrEmpty(filePath))
            {
                string key = System.IO.Path.GetFullPath(filePath);
                if (_cache.TryGetValue(key, out var cached)) return cached;
            }
            return LoadCore(filePath, strict: options.Strict, sheetName: options.SheetName, cacheKey: options.UseCache ? filePath : null);
        }

        /// <summary>便捷重载：strict=true 时校验失败抛异常(走缓存)。</summary>
        public static TableReader<T> Load(string filePath, bool strict, string sheetName = null)
        {
            return Load(filePath, new TableLoadOptions { Strict = strict, UseCache = true, SheetName = sheetName });
        }

        public static TableReader<T> Load(TextAsset asset) => Load(asset, TableLoadOptions.Default);

        public static TableReader<T> Load(TextAsset asset, TableLoadOptions options)
        {
            if (asset == null) return null;
            var data = TableData.LoadFromString(asset.text, asset.name, options.Strict);
            if (data == null) return null;
            return Build(data, options.Strict);
        }

        public static TableReader<T> LoadFromString(string text, string tableName, bool strict = false)
        {
            var data = TableData.LoadFromString(text, tableName, strict);
            if (data == null) return null;
            return Build(data, strict);
        }

        /// <summary>从 StreamingAssets 异步加载(全平台)。yield 返回 reader。
        /// 用法：IEnumerator Foo() { var r = new TableReader&lt;X&gt;(); yield return TableReader&lt;X&gt;.LoadFromStreamingAssets("Tables/x.csv", reader => r = ...); }</summary>
        public static IEnumerator LoadFromStreamingAssets(string relPath, Action<TableReader<T>> onComplete, bool strict = false, string sheetName = null)
        {
            TableReader<T> result = null;
            yield return TableWebLoader.LoadStreamingAssetTextCoroutine(relPath, text =>
            {
                if (string.IsNullOrEmpty(text)) { result = null; return; }
                string name = System.IO.Path.GetFileNameWithoutExtension(relPath);
                result = LoadFromString(text, name, strict);
            });
            onComplete?.Invoke(result);
        }

        private static TableReader<T> LoadCore(string filePath, bool strict, string sheetName, string cacheKey)
        {
            var data = TableData.Load(filePath, strict, sheetName);
            if (data == null) return null;
            var reader = Build(data, strict);
            if (reader != null && cacheKey != null)
            {
                string key = System.IO.Path.GetFullPath(cacheKey);
                _cache[key] = reader;
            }
            return reader;
        }

        public static void ClearCache() { _cache.Clear(); }
        public static void ClearCache(string filePath)
        {
            if (string.IsNullOrEmpty(filePath)) return;
            _cache.Remove(System.IO.Path.GetFullPath(filePath));
        }

        private static TableReader<T> Build(TableData data, bool strict)
        {
            var reader = new TableReader<T>(data);
            reader.BuildFieldMaps();
            reader.ValidateClassMatch(strict);
            reader.Materialize();
            reader.BuildAllIndexes();
            return reader;
        }

        private void BuildFieldMaps()
        {
            var fields = typeof(T).GetFields(BindingFlags.Public | BindingFlags.Instance);
            foreach (var fi in fields)
            {
                string colName = fi.Name;
                var attr = fi.GetCustomAttribute<TableFieldAttribute>();
                if (attr != null && !string.IsNullOrEmpty(attr.ColumnName)) colName = attr.ColumnName;
                _fieldMaps[colName] = new FieldMap { Field = fi, FieldType = fi.FieldType, ColumnName = colName };
            }
        }

        private void ValidateClassMatch(bool strict)
        {
            foreach (var col in _data.FieldNames)
            {
                if (!_fieldMaps.ContainsKey(col))
                    _data.Validation.Warnings.Add($"表格列 '{col}' 在类 {typeof(T).Name} 中没有对应字段(类可能需要重新生成)");
            }
            foreach (var kv in _fieldMaps)
            {
                if (!_data.FieldNames.Contains(kv.Value.ColumnName))
                    _data.Validation.Warnings.Add($"类字段 '{kv.Value.Field.Name}' 在表格中没有对应列(将使用默认值)");
            }
            for (int i = 0; i < _data.FieldNames.Count; i++)
            {
                string col = _data.FieldNames[i];
                Type tableType = _data.FieldTypes[i];
                if (_fieldMaps.TryGetValue(col, out var map))
                {
                    if (!TypeCompatible(map.FieldType, tableType))
                        _data.Validation.Warnings.Add($"字段 '{col}' 类型不一致: 类={TableTypeConverter.CSharpName(map.FieldType)} 表={TableTypeConverter.CSharpName(tableType)}");
                }
            }
            if (strict && !_data.Validation.IsValid) throw new TableValidationException(_data.Validation);
        }

        private static bool TypeCompatible(Type fieldType, Type tableType)
        {
            if (fieldType == tableType) return true;
            if (IsNumeric(fieldType) && IsNumeric(tableType)) return true;
            return false;
        }

        private static bool IsNumeric(Type t) => t == typeof(int) || t == typeof(long) || t == typeof(float) || t == typeof(double);

        private void Materialize()
        {
            foreach (var row in _data.Rows)
            {
                var item = new T();
                foreach (var kv in _fieldMaps)
                {
                    var map = kv.Value;
                    string raw = row.GetValue(kv.Key);
                    if (raw == null) continue;
                    var v = TableTypeConverter.Convert(raw, map.FieldType, out var err);
                    if (err == null) map.Field.SetValue(item, v);
                }
                _items.Add(item);
            }
        }

        private void BuildAllIndexes()
        {
            foreach (var kv in _fieldMaps) BuildIndex(kv.Key);
        }

        public void BuildIndex(string fieldName)
        {
            if (_indexes.ContainsKey(fieldName)) return;
            if (!_fieldMaps.TryGetValue(fieldName, out var map)) return;

            Type keyType = map.FieldType;
            var idx = new Dictionary<object, List<T>>(FieldComparer.Instance);
            for (int i = 0; i < _data.Rows.Count; i++)
            {
                string raw = _data.Rows[i].GetValue(fieldName);
                if (string.IsNullOrEmpty(raw)) continue;
                if (!TableTypeConverter.TryConvert(raw, keyType, out var key, out _) || key == null) continue;
                if (!idx.TryGetValue(key, out var bucket))
                {
                    bucket = new List<T>();
                    idx[key] = bucket;
                }
                bucket.Add(_items[i]);
            }
            _indexes[fieldName] = idx;
        }

        public List<T> FindBy(string fieldName, object value)
        {
            if (value == null) return new List<T>();
            if (!_indexes.TryGetValue(fieldName, out var idx))
            {
                BuildIndex(fieldName);
                _indexes.TryGetValue(fieldName, out idx);
            }
            if (idx == null) return new List<T>();

            object key = value;
            if (_fieldMaps.TryGetValue(fieldName, out var map) && key.GetType() != map.FieldType)
                key = NormalizeValue(value, map.FieldType);
            return idx.TryGetValue(key, out var bucket) ? bucket : new List<T>();
        }

        public T FindFirst(string fieldName, object value)
        {
            var list = FindBy(fieldName, value);
            return list.Count > 0 ? list[0] : null;
        }

        public List<T> Where(Predicate<T> predicate)
        {
            var result = new List<T>();
            foreach (var item in _items)
                if (predicate == null || predicate(item)) result.Add(item);
            return result;
        }

        private static object NormalizeValue(object value, Type keyType)
        {
            if (value == null) return null;
            if (value.GetType() == keyType) return value;
            if (value is string s)
            {
                if (TableTypeConverter.TryConvert(s, keyType, out var v, out _)) return v;
                return value;
            }
            if (IsNumeric(keyType) && IsNumeric(value.GetType()))
            {
                try { return Convert.ChangeType(value, keyType); }
                catch { return value; }
            }
            return value;
        }

        private sealed class FieldComparer : IEqualityComparer<object>
        {
            public static readonly FieldComparer Instance = new FieldComparer();
            public new bool Equals(object x, object y)
            {
                if (ReferenceEquals(x, y)) return true;
                if (x == null || y == null) return false;
                if (x is string sx && y is string sy) return sx == sy;
                if (IsNumeric(x.GetType()) && IsNumeric(y.GetType()))
                    return Convert.ToDouble(x) == Convert.ToDouble(y);
                return x.Equals(y);
            }
            public int GetHashCode(object obj)
            {
                if (obj == null) return 0;
                if (obj is string s) return s.GetHashCode();
                if (IsNumeric(obj.GetType())) return Convert.ToDouble(obj).GetHashCode();
                return obj.GetHashCode();
            }
        }
    }
}
