using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using CodeLittleDog.TableTool;

namespace CodeLittleDog.TableTool.Editor
{
    /// <summary>
    /// xlsx -> csv 导出器：把 xlsx 数据表导出为 csv/tsv，供 WebGL/iOS/Android APK 内运行时读取。
    /// 菜单：Tools/Table CSV Exporter
    /// </summary>
    public class TableCsvExporterWindow : EditorWindow
    {
        private string sourcePath = "";
        private string sheetName = "";
        private string outputFolder = "";
        private bool useTab = false;

        [MenuItem("Tools/Table CSV Exporter")]
        public static void ShowWindow() => GetWindow<TableCsvExporterWindow>("表格 CSV 导出器");

        private void OnEnable()
        {
            if (string.IsNullOrEmpty(outputFolder))
                outputFolder = Application.dataPath + "/StreamingAssets";
        }

        private void OnGUI()
        {
            GUILayout.Label("xlsx -> csv 导出器 (WebGL/iOS 准备)", EditorStyles.boldLabel);
            EditorGUILayout.Space(8);

            EditorGUILayout.HelpBox(
                "WebGL/iOS 平台不支持运行时解析 xlsx。\n" +
                "开发期在 Editor 配置 xlsx，打包前用本工具导出为 csv/tsv 放到 StreamingAssets，" +
                "运行时用 TableReader<T>.LoadFromStreamingAssets(\"Tables/x.csv\", ...) 异步读取。",
                MessageType.Info);
            EditorGUILayout.Space(6);

            EditorGUILayout.LabelField("源 xlsx 文件", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            sourcePath = EditorGUILayout.TextField(sourcePath);
            if (GUILayout.Button("选择", GUILayout.Width(60)))
            {
                string p = EditorUtility.OpenFilePanel("选择 xlsx", Application.dataPath, "xlsx");
                if (!string.IsNullOrEmpty(p)) sourcePath = p;
            }
            GUILayout.EndHorizontal();
            sheetName = EditorGUILayout.TextField("Sheet 名(可空=第一个)", sheetName);

            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("输出目录(建议 StreamingAssets)", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            outputFolder = EditorGUILayout.TextField(outputFolder);
            if (GUILayout.Button("选择", GUILayout.Width(60)))
            {
                string p = EditorUtility.OpenFolderPanel("选择输出目录", Application.dataPath, "");
                if (!string.IsNullOrEmpty(p)) outputFolder = p;
            }
            GUILayout.EndHorizontal();
            useTab = EditorGUILayout.Toggle("用 Tab 分隔(TSV)", useTab);

            EditorGUILayout.Space(10);
            GUI.enabled = !string.IsNullOrEmpty(sourcePath) && !string.IsNullOrEmpty(outputFolder);
            if (GUILayout.Button("导出 CSV", GUILayout.Height(36))) Export();
            GUI.enabled = true;
        }

        private void Export()
        {
            var data = TableData.Load(sourcePath, strict: false, sheetName: string.IsNullOrEmpty(sheetName) ? null : sheetName);
            if (data == null)
            {
                EditorUtility.DisplayDialog("错误", "读取 xlsx 失败，请检查文件与 Sheet 名。", "确定");
                return;
            }

            if (!Directory.Exists(outputFolder)) Directory.CreateDirectory(outputFolder);
            string outName = data.TableName + (useTab ? ".tsv" : ".csv");
            string outPath = Path.Combine(outputFolder, outName);
            File.WriteAllText(outPath, Serialize(data, useTab ? '\t' : ','), Encoding.UTF8);

            string relative = outPath;
            if (outPath.StartsWith(Application.dataPath))
            {
                relative = "Assets" + outPath.Substring(Application.dataPath.Length).Replace('\\', '/');
                AssetDatabase.ImportAsset(relative, ImportAssetOptions.ForceUpdate);
                AssetDatabase.Refresh();
            }

            EditorUtility.DisplayDialog("完成", $"已导出: {outPath}\n行数: {data.Rows.Count}", "确定");
            Debug.Log($"[TableCsvExporter] 导出 {outPath}");
        }

        /// <summary>把 TableData 序列化为 csv/tsv。含逗号/引号/换行的单元格用引号包裹并转义双引号。</summary>
        private static string Serialize(TableData data, char sep)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < data.FieldNames.Count; i++)
            {
                if (i > 0) sb.Append(sep);
                string h = $"{data.FieldNames[i]}({TableTypeConverter.TypeName(data.FieldTypes[i])})";
                sb.Append(EscapeCell(h, sep));
            }
            sb.AppendLine();
            foreach (var row in data.Rows)
            {
                for (int i = 0; i < data.FieldNames.Count; i++)
                {
                    if (i > 0) sb.Append(sep);
                    sb.Append(EscapeCell(row.GetValue(data.FieldNames[i]) ?? "", sep));
                }
                sb.AppendLine();
            }
            return sb.ToString();
        }

        private static string EscapeCell(string v, char sep)
        {
            if (v.IndexOfAny(new[] { sep, '"', '\n', '\r' }) < 0) return v;
            return "\"" + v.Replace("\"", "\"\"") + "\"";
        }
    }
}
