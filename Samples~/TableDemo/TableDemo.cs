using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using CodeLittleDog.TableTool;

/// <summary>
/// 表格工具使用演示。挂到任意 GameObject 上运行，控制台查看查询结果。
/// 演示：1) Resources 同步加载(CSV) 2) StreamingAssets 异步加载(WebGL 友好) 3) 查找 + 校验。
/// </summary>
public class TableDemo : MonoBehaviour
{
    // 该类可由菜单 Tools > Table Class Generator 用模板表自动生成；此处内联以便演示独立运行。
    public class PlayerConfig
    {
        public string name;
        public int age;
        public float score;
    }

    private void Start()
    {
        // 1) Resources 同步加载(全平台)
        var asset = Resources.Load<TextAsset>("Tables/PlayerConfig");
        if (asset == null)
        {
            Debug.LogError("[TableDemo] 未找到 Resources/Tables/PlayerConfig");
            return;
        }

        Debug.Log("===== 1) 无类型 TableData：按字段值查找行 =====");
        var data = TableData.Load(asset, strict: false);
        data.Validation.Log();
        TableRow row = data.FindFirst("name", "lisi");
        if (row != null)
            Debug.Log($"name=lisi => age={row.GetValue("age")}, score={row.GetValue("score")}");

        Debug.Log("===== 2) 泛型 TableReader<PlayerConfig>：强类型 + O(1) 查找 =====");
        var reader = TableReader<PlayerConfig>.Load(asset);
        if (reader == null) return;

        if (reader.Validation.Warnings.Count > 0)
            foreach (var w in reader.Validation.Warnings) Debug.LogWarning(w);

        PlayerConfig p1 = reader.FindFirst("name", "zhangsan");
        if (p1 != null)
            Debug.Log($"FindFirst name=zhangsan => age={p1.age} score={p1.score}");

        List<PlayerConfig> age20 = reader.FindBy("age", 20);
        Debug.Log($"FindBy age=20 => 命中 {age20.Count} 条");

        List<PlayerConfig> s85 = reader.FindBy("score", 85f);
        Debug.Log($"FindBy score=85 => 命中 {s85.Count} 条");

        Debug.Log($"总行数: {reader.Count}");

        // 2) StreamingAssets 异步加载(WebGL/Android APK 内文件用这个)
        // 把 csv 放到 Assets/StreamingAssets/Tables/PlayerConfig.csv 后启用：
        // StartCoroutine(LoadFromStreamingAssets());
    }

    private IEnumerator LoadFromStreamingAssets()
    {
        yield return TableReader<PlayerConfig>.LoadFromStreamingAssets(
            "Tables/PlayerConfig.csv",
            reader =>
            {
                if (reader == null) { Debug.LogError("StreamingAssets 加载失败"); return; }
                PlayerConfig p = reader.FindFirst("name", "lisi");
                Debug.Log($"[StreamingAssets] name=lisi => age={p.age} score={p.score}");
            });
    }
}
