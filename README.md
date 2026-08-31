# Table Tool

Excel/CSV 表格读取写入工具：模板表生成类 + 泛型 `TableReader<T>` + O(1) 查找 + 数据格式校验。

## 特性

- **模板表生成类**：首行字段定义 `name(string)` / `age(int)`，菜单自动生成 C# 类。
- **泛型读取**：`TableReader<生成的类>.Load(path)`，泛型就是提前用表格生成的类。
- **O(1) 查找**：解析一次后每字段建哈希索引，`FindBy`/`FindFirst` 即时返回。
- **数据校验**：表头格式、单元格类型、类表一致性校验；严格模式抛异常。
- **xlsx 原生读取**：零第三方依赖(`System.IO.Compression` + Xml)。
- **跨平台**：见下表。

## 平台支持

| 平台 | CSV/TSV (TextAsset/Resources) | CSV/TSV (StreamingAssets, 异步) | xlsx 外部文件 |
|---|---|---|---|
| Windows / Mac Standalone | ✅ | ✅ | ✅ |
| Android | ✅ | ✅(APK 内文件推荐这个) | ✅(外部存储路径) |
| iOS | ✅ | ✅ | ❌(IL2CPP 不支持 System.IO.Compression) |
| WebGL | ✅ | ✅ | ❌(无文件系统 + Compression 不可用) |

**WebGL/iOS 工作流**：开发期用 xlsx 配置，打包前用菜单 `Tools > Table CSV Exporter` 把 xlsx 导出为 csv 放 `StreamingAssets`，运行时用 `TableReader<T>.LoadFromStreamingAssets(...)` 异步读取。

## 安装

通过 Package Manager > Add package from disk，选 `package.json`；或把本目录放到 `Packages/` 下。

## 文件结构

```
com.codelittledog.tabletool/
├── package.json
├── Runtime/
│   ├── CodeLittleDog.TableTool.Runtime.asmdef
│   ├── TableConfigAttribute.cs    # 特性 + TableLoadOptions
│   ├── TableTypeConverter.cs      # 类型转换 + 错误信息
│   ├── TableSource.cs              # ITableSource + TextTableSource + ExcelXlsxSource(非WebGL)
│   ├── TableData.cs                # 解析/校验/索引/查找/写入 + StreamingAssets 异步加载
│   ├── TableReader.cs              # 泛型 TableReader<T> + 缓存 + 一致性校验
│   └── TableWebLoader.cs           # UnityWebRequest 跨平台文本加载
├── Editor/
│   ├── CodeLittleDog.TableTool.Editor.asmdef
│   ├── TableClassGenerator.cs      # Tools > Table Class Generator
│   └── TableCsvExporter.cs         # Tools > Table CSV Exporter (xlsx->csv)
├── Samples~/TableDemo/            # 示例(在 PM 中 Import)
└── README.md
```

## 工作流

### 1) 配置模板表(放 Assets 下)
Excel 首行字段定义：`name(string)  age(int)  score(float)`，下面填示例数据，存 `.xlsx`。

### 2) 生成类
`Tools > Table Class Generator` → 选模板表 → 预览 → 生成 `.cs`。

### 3) 配置数据表(放外部路径或 StreamingAssets)
结构与模板表一致。

### 4) 运行时读取

```csharp
using CodeLittleDog.TableTool;

// 桌面/Android 外部路径
var reader = TableReader<PlayerConfig>.Load("D:/GameData/PlayerConfig.xlsx");
PlayerConfig p = reader.FindFirst("name", "zhangsan");   // O(1)

// 严格模式(校验失败抛异常)
var reader = TableReader<PlayerConfig>.Load(path, strict: true);

// WebGL/iOS：StreamingAssets 异步(协程)
IEnumerator Load() {
    yield return TableReader<PlayerConfig>.LoadFromStreamingAssets(
        "Tables/PlayerConfig.csv",
        r => { if (r != null) Debug.Log(r.FindFirst("name","lisi").age); });
}

// Resources 同步(CSV/TSV, 全平台)
var asset = Resources.Load<TextAsset>("Tables/PlayerConfig");
var reader = TableReader<PlayerConfig>.Load(asset);

// 校验结果
reader.Validation.Log();
```

## 命名空间

所有运行时类型在 `CodeLittleDog.TableTool`，使用前 `using CodeLittleDog.TableTool;`。

## 限制

- 不支持 `.xls`(旧二进制格式)，请另存为 `.xlsx`。
- 不支持合并单元格(取左上格)。
- 公式读保存时缓存的值，改完务必保存文件。
- xlsx 解析在 WebGL/iOS 不可用 → 用 csv。
