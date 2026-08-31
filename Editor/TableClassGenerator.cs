using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using CodeLittleDog.TableTool;

namespace CodeLittleDog.TableTool.Editor
{
    /// <summary>
    /// 模板表 -> 公共类 生成器：读取「模板表」(.xlsx/.csv/.tsv/.txt)，按首行字段定义生成 C# 类。
    /// 菜单：Tools/Table Class Generator
    /// </summary>
    public class TableClassGeneratorWindow : EditorWindow
    {
        private string sourcePath = "";
        private string sheetName = "";
        private string outputFolder = "";
        private string classNamespace = "";
        private Vector2 scroll;
        private TableData preview;

        [MenuItem("Tools/Table Class Generator")]
        public static void ShowWindow() => GetWindow<TableClassGeneratorWindow>("表格类生成器");

        private void OnEnable()
        {
            if (string.IsNullOrEmpty(outputFolder))
                outputFolder = Application.dataPath + "/Script/Generated";
        }

        private void OnGUI()
        {
            GUILayout.Label("模板表 -> 公共类 生成器", EditorStyles.boldLabel);
            EditorGUILayout.Space(8);

            EditorGUILayout.LabelField("模板表文件 (.xlsx/.csv/.tsv/.txt，建议放 Assets 下)", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            sourcePath = EditorGUILayout.TextField(sourcePath);
            if (GUILayout.Button("选择", GUILayout.Width(60)))
            {
                string p = EditorUtility.OpenFilePanel("选择模板表", Application.dataPath, "xlsx,csv,tsv,txt");
                if (!string.IsNullOrEmpty(p)) sourcePath = p;
            }
            GUILayout.EndHorizontal();
            sheetName = EditorGUILayout.TextField("Sheet 名(可空=第一个)", sheetName);

            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("输出目录", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            outputFolder = EditorGUILayout.TextField(outputFolder);
            if (GUILayout.Button("选择", GUILayout.Width(60)))
            {
                string p = EditorUtility.OpenFolderPanel("选择输出目录", Application.dataPath, "");
                if (!string.IsNullOrEmpty(p)) outputFolder = p;
            }
            GUILayout.EndHorizontal();

            EditorGUILayout.Space(6);
            classNamespace = EditorGUILayout.TextField("命名空间(可空)", classNamespace);

            EditorGUILayout.Space(10);

            if (GUILayout.Button("预览字段", GUILayout.Width(90))) PreviewTable();

            if (preview != null)
            {
                EditorGUILayout.LabelField($"类名: {preview.TableName}    行数: {preview.Rows.Count}", EditorStyles.boldLabel);
                if (!preview.Validation.IsValid)
                    EditorGUILayout.HelpBox($"校验有 {preview.Validation.Errors.Count} 处错误(见控制台)。", MessageType.Warning);
                scroll = EditorGUILayout.BeginScrollView(scroll, GUILayout.Height(140));
                for (int i = 0; i < preview.FieldNames.Count; i++)
                {
                    string type = TableTypeConverter.CSharpName(preview.FieldTypes[i]);
                    EditorGUILayout.LabelField($"    public {type} {preview.FieldNames[i]};");
                }
                EditorGUILayout.EndScrollView();
            }

            EditorGUILayout.Space(10);

            GUI.enabled = !string.IsNullOrEmpty(sourcePath) && !string.IsNullOrEmpty(outputFolder) && preview != null;
            if (GUILayout.Button("生成 .cs 类", GUILayout.Height(36))) Generate();
            GUI.enabled = true;
        }

        private void PreviewTable()
        {
            if (string.IsNullOrEmpty(sourcePath)) return;
            preview = TableData.Load(sourcePath, strict: false, sheetName: string.IsNullOrEmpty(sheetName) ? null : sheetName);
            if (preview != null && !preview.Validation.IsValid) preview.Validation.Log();
        }

        private void Generate()
        {
            if (preview == null) PreviewTable();
            if (preview == null)
            {
                EditorUtility.DisplayDialog("错误", "请先选择有效的模板表并预览。", "确定");
                return;
            }

            string className = preview.TableName;
            var sb = new StringBuilder();
            sb.AppendLine("using System.Collections.Generic;");
            sb.AppendLine();
            bool useNs = !string.IsNullOrEmpty(classNamespace);
            if (useNs) { sb.AppendLine($"namespace {classNamespace}"); sb.AppendLine("{"); }
            sb.AppendLine($"/// <summary>由模板表 {Path.GetFileName(sourcePath)} 自动生成，请勿手动修改。 </summary>");
            sb.AppendLine($"public class {className}");
            sb.AppendLine("{");
            for (int c = 0; c < preview.FieldNames.Count; c++)
            {
                string type = TableTypeConverter.CSharpName(preview.FieldTypes[c]);
                string fname = preview.FieldNames[c];
                sb.AppendLine($"    public {type} {fname};");
            }
            sb.AppendLine("}");
            if (useNs) sb.AppendLine("}");

            if (!Directory.Exists(outputFolder)) Directory.CreateDirectory(outputFolder);
            string outPath = Path.Combine(outputFolder, className + ".cs");
            File.WriteAllText(outPath, sb.ToString(), Encoding.UTF8);

            string relative = "Assets" + outPath.Replace('\\', '/').Substring(Application.dataPath.Length);
            AssetDatabase.ImportAsset(relative, ImportAssetOptions.ForceUpdate);
            AssetDatabase.Refresh();

            EditorUtility.DisplayDialog("完成", $"已生成: {outPath}\n类名: {className}", "确定");
            Debug.Log($"[TableClassGenerator] 生成 {outPath}");
        }
    }
}
