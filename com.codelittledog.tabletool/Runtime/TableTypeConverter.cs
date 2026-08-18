using System;
using System.Globalization;
using UnityEngine;

namespace CodeLittleDog.TableTool
{
    /// <summary>
    /// 类型名与 System.Type 的互转 + 字符串到目标类型的转换(带详细错误信息)。
    /// 支持类型：string / int / long / float / double / bool。
    /// </summary>
    public static class TableTypeConverter
    {
        public static Type ResolveType(string typeName)
        {
            switch ((typeName ?? "").Trim().ToLowerInvariant())
            {
                case "string": case "str": return typeof(string);
                case "int": case "i32": return typeof(int);
                case "long": case "i64": return typeof(long);
                case "float": case "f32": return typeof(float);
                case "double": case "f64": return typeof(double);
                case "bool": case "boolean": return typeof(bool);
                default: return typeof(string);
            }
        }

        public static string TypeName(Type t)
        {
            if (t == typeof(int)) return "int";
            if (t == typeof(long)) return "long";
            if (t == typeof(float)) return "float";
            if (t == typeof(double)) return "double";
            if (t == typeof(bool)) return "bool";
            return "string";
        }

        public static string CSharpName(Type t)
        {
            if (t == typeof(int)) return "int";
            if (t == typeof(long)) return "long";
            if (t == typeof(float)) return "float";
            if (t == typeof(double)) return "double";
            if (t == typeof(bool)) return "bool";
            if (t == typeof(string)) return "string";
            return t.Name;
        }

        public static bool TryConvert(string raw, Type targetType, out object result, out string errorMessage)
        {
            result = null;
            errorMessage = null;
            if (targetType == typeof(string)) { result = raw ?? ""; return true; }

            string s = raw == null ? "" : raw.Trim();
            if (s.Length == 0)
            {
                result = targetType.IsValueType ? Activator.CreateInstance(targetType) : null;
                return true;
            }
            try
            {
                if (targetType == typeof(int)) { result = int.Parse(s, CultureInfo.InvariantCulture); return true; }
                if (targetType == typeof(long)) { result = long.Parse(s, CultureInfo.InvariantCulture); return true; }
                if (targetType == typeof(float)) { result = float.Parse(s, CultureInfo.InvariantCulture); return true; }
                if (targetType == typeof(double)) { result = double.Parse(s, CultureInfo.InvariantCulture); return true; }
                if (targetType == typeof(bool))
                {
                    string low = s.ToLowerInvariant();
                    if (low == "true" || low == "1" || low == "yes" || low == "y") { result = true; return true; }
                    if (low == "false" || low == "0" || low == "no" || low == "n") { result = false; return true; }
                    result = bool.Parse(s);
                    return true;
                }
            }
            catch (Exception ex)
            {
                errorMessage = $"值 '{raw}' 无法转为 {CSharpName(targetType)}: {ex.Message}";
                return false;
            }
            errorMessage = $"不支持的类型 {CSharpName(targetType)}";
            return false;
        }

        public static object Convert(string raw, Type targetType, out string errorMessage)
        {
            if (TryConvert(raw, targetType, out var v, out errorMessage)) return v;
            return targetType.IsValueType ? Activator.CreateInstance(targetType) : null;
        }

        public static object Convert(string raw, Type targetType)
        {
            var v = Convert(raw, targetType, out var err);
            if (err != null) Debug.LogWarning($"[TableData] {err}，使用默认值。");
            return v;
        }
    }
}
