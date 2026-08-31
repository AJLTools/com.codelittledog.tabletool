# Changelog

## [1.0.0] - 2026-08-18

### Added
- 表格工具首个 package 版本。
- 模板表(xlsx/csv)生成 C# 类：`Tools > Table Class Generator`。
- 泛型 `TableReader<T>`：反射填充 + 缓存 + 类表一致性校验 + O(1) 查找。
- 无类型 `TableData`：解析/校验/索引/查找/写入。
- 原生 xlsx 读取(零依赖，`System.IO.Compression` + Xml)，支持多 Sheet、共享字符串、内联字符串、数字、布尔、公式缓存值。
- 数据格式校验：表头、单元格类型、类表一致性；非严格/严格两种模式。
- 跨平台：`TableWebLoader` 用 `UnityWebRequest` 从 StreamingAssets 异步加载(Android APK / WebGL / iOS / 桌面均可用)。
- xlsx → csv 导出器：`Tools > Table CSV Exporter`，供 WebGL/iOS 打包前转换。
- 平台宏隔离：xlsx 解析代码用 `#if !UNITY_WEBGL && !UNITY_IOS && !UNITY_TVOS` 包裹，确保 WebGL/iOS 编译通过。
- Samples：TableDemo 演示 Resources 同步加载 + StreamingAssets 异步加载 + 查找校验。

### 平台支持
- Windows / macOS Standalone：xlsx + csv 全功能。
- Android：xlsx(外部路径) + csv(外部/StreamingAssets)。
- iOS / WebGL：csv(Resources/StreamingAssets)，不支持 xlsx。
