using System;

namespace CodeLittleDog.TableTool
{
    /// <summary>
    /// 表格工具：特性定义。
    ///
    /// 工作流：
    ///   1) 策划在 Excel(.xlsx) 中配置「模板表」(首行字段定义)，放在 Unity Assets 路径下；
    ///   2) 用菜单 Tools > Table Class Generator 读取模板表，生成公共类 C#；
    ///   3) 策划填好「数据表」(.xlsx) 放到外部路径；
    ///   4) 运行时 TableReader&lt;生成的类&gt;.Load(外部路径) 读取并按字段值 O(1) 查找。
    ///
    /// 表格格式（首行=字段定义，其余=数据，一格一个值）：
    ///   name(string)	age(int)	score(float)
    ///   zhangsan	20	90.5
    ///   lisi	21	85
    /// </summary>
    public class TableConfigAttribute : Attribute
    {
        public string SheetName { get; }
        public string TableName { get; }
        public TableConfigAttribute(string sheetName = null, string tableName = null)
        {
            SheetName = sheetName;
            TableName = tableName;
        }
    }

    /// <summary>标记类字段与表格列名的映射。不指定则直接用字段名作为列名。</summary>
    [AttributeUsage(AttributeTargets.Field)]
    public class TableFieldAttribute : Attribute
    {
        public string ColumnName { get; }
        public TableFieldAttribute(string columnName = null) { ColumnName = columnName; }
    }

    /// <summary>读取选项：严格模式、缓存、指定 Sheet 名。</summary>
    public struct TableLoadOptions
    {
        /// <summary>严格模式：校验失败抛 TableValidationException。默认 false。</summary>
        public bool Strict;
        /// <summary>是否使用静态缓存(同路径只解析一次)。默认 true。</summary>
        public bool UseCache;
        /// <summary>读取 xlsx 时指定的 Sheet 名。为空取第一个 Sheet。</summary>
        public string SheetName;

        public static TableLoadOptions Default => new TableLoadOptions { Strict = false, UseCache = true, SheetName = null };
        public static TableLoadOptions StrictMode => new TableLoadOptions { Strict = true, UseCache = true, SheetName = null };
    }
}
