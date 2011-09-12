﻿using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Text;
using NewLife.Reflection;
using XCode.Exceptions;

namespace XCode.DataAccessLayer
{
    partial class DbMetaData
    {
        #region 常量
        protected static class CollectionNames
        {
            public const String Tables = "Tables";
            public const String Indexes = "Indexes";
            public const String IndexColumns = "IndexColumns";
            public const String Databases = "Databases";
            public const String Columns = "Columns";
        }
        #endregion

        #region 表构架
        /// <summary>取得所有表构架</summary>
        /// <returns></returns>
        public List<IDataTable> GetTables()
        {
            try
            {
                return OnGetTables();
            }
            catch (DbException ex)
            {
                throw new XDbMetaDataException(this, "取得所有表构架出错！", ex);
            }
        }

        /// <summary>取得所有表构架</summary>
        /// <returns></returns>
        protected virtual List<IDataTable> OnGetTables()
        {
            DataTable dt = GetSchema(CollectionNames.Tables, null);
            if (dt == null || dt.Rows == null || dt.Rows.Count < 1) return null;

            // 默认列出所有表
            DataRow[] rows = new DataRow[dt.Rows.Count];
            dt.Rows.CopyTo(rows, 0);
            return GetTables(rows);
        }

        /// <summary>根据数据行取得数据表</summary>
        /// <param name="rows">数据行</param>
        /// <returns></returns>
        protected List<IDataTable> GetTables(DataRow[] rows)
        {
            _indexes = GetSchema(CollectionNames.Indexes, null);
            _indexColumns = GetSchema(CollectionNames.IndexColumns, null);

            List<IDataTable> list = new List<IDataTable>();
            foreach (DataRow dr in rows)
            {
                #region 基本属性
                IDataTable table = DAL.CreateTable();
                table.Name = GetDataRowValue<String>(dr, "TABLE_NAME");

                // 顺序、编号
                Int32 id = 0;
                if (TryGetDataRowValue<Int32>(dr, "TABLE_ID", out id))
                    table.ID = id;
                else
                    table.ID = list.Count + 1;

                // 描述
                table.Description = GetDataRowValue<String>(dr, "DESCRIPTION");

                // 拥有者
                table.Owner = GetDataRowValue<String>(dr, "OWNER");

                // 是否视图
                table.IsView = String.Equals("View", GetDataRowValue<String>(dr, "TABLE_TYPE"), StringComparison.OrdinalIgnoreCase);

                table.DbType = Database.DbType;
                #endregion

                #region 字段及修正
                // 字段的获取可能有异常，但不应该影响整体架构的获取
                try
                {
                    List<IDataColumn> columns = GetFields(table);
                    if (columns != null && columns.Count > 0) table.Columns.AddRange(columns);

                    List<IDataIndex> indexes = GetIndexes(table);
                    if (indexes != null && indexes.Count > 0) table.Indexes.AddRange(indexes);

                    // 先修正一次关系数据
                    table.Fix();
                }
                catch (Exception ex)
                {
                    if (DAL.Debug) DAL.WriteLog(ex.ToString());
                }

                FixTable(table, dr);

                list.Add(table);
                #endregion
            }

            #region 表间关系处理
            // 某字段名，为另一个表的（表名+单主键名）形式时，作为关联字段处理
            foreach (IDataTable table in list)
            {
                foreach (IDataTable rtable in list)
                {
                    if (table != rtable) table.Connect(rtable);
                }
            }

            // 因为可能修改了表间关系，再修正一次
            foreach (IDataTable table in list)
            {
                table.Fix();
            }
            #endregion

            _indexes = null;

            return list;
        }

        /// <summary>修正表</summary>
        /// <param name="table"></param>
        /// <param name="dr"></param>
        protected virtual void FixTable(IDataTable table, DataRow dr) { }
        #endregion

        #region 字段架构
        /// <summary>取得指定表的所有列构架</summary>
        /// <param name="table"></param>
        /// <returns></returns>
        protected virtual List<IDataColumn> GetFields(IDataTable table)
        {
            DataTable dt = GetSchema(CollectionNames.Columns, new String[] { null, null, table.Name });

            DataRow[] drs = null;
            if (dt.Columns.Contains("ORDINAL_POSITION"))
                drs = dt.Select("", "ORDINAL_POSITION");
            else
                drs = dt.Select("");

            List<IDataColumn> list = GetFields(table, drs);

            return list;
        }

        /// <summary>获取指定表的字段</summary>
        /// <param name="table"></param>
        /// <param name="rows"></param>
        /// <returns></returns>
        protected virtual List<IDataColumn> GetFields(IDataTable table, DataRow[] rows)
        {
            //Dictionary<DataRow, String> pks = GetPrimaryKeys(table.Name);

            List<IDataColumn> list = new List<IDataColumn>();
            // 开始序号
            Int32 startIndex = 0;
            foreach (DataRow dr in rows)
            {
                //IDataColumn field = table.CreateColumn();
                IDataColumn field = table.CreateColumn();

                // 序号
                Int32 n = 0;
                if (TryGetDataRowValue<Int32>(dr, "ORDINAL_POSITION", out n))
                    field.ID = n;
                else if (TryGetDataRowValue<Int32>(dr, "ID", out n))
                    field.ID = n;
                // 如果从0开始，则所有需要同步增加；如果所有字段序号都是0，则按照先后顺序
                if (field.ID == 0)
                {
                    startIndex++;
                    //field.ID = startIndex;
                }
                if (startIndex > 0) field.ID += startIndex;

                // 名称
                field.Name = GetDataRowValue<String>(dr, "COLUMN_NAME");

                // 标识、主键
                Boolean b;
                if (TryGetDataRowValue<Boolean>(dr, "AUTOINCREMENT", out  b))
                    field.Identity = b;

                if (TryGetDataRowValue<Boolean>(dr, "PRIMARY_KEY", out  b))
                    field.PrimaryKey = b;
                //else
                //    field.PrimaryKey = pks != null && pks.ContainsValue(field.Name);

                // 原始数据类型
                //field.RawType = GetDataRowValue<String>(dr, "DATA_TYPE");
                String str;
                if (TryGetDataRowValue<String>(dr, "DATA_TYPE", out str))
                    field.RawType = str;
                else if (TryGetDataRowValue<String>(dr, "DATATYPE", out str))
                    field.RawType = str;
                else if (TryGetDataRowValue<String>(dr, "COLUMN_DATA_TYPE", out str))
                    field.RawType = str;

                // 是否Unicode
                if (Database is DbBase) field.IsUnicode = (Database as DbBase).IsUnicode(field.RawType);

                // 精度
                if (TryGetDataRowValue<Int32>(dr, "NUMERIC_PRECISION", out n))
                    field.Precision = n;
                else if (TryGetDataRowValue<Int32>(dr, "DATETIME_PRECISION", out n))
                    field.Precision = n;
                else if (TryGetDataRowValue<Int32>(dr, "PRECISION", out n))
                    field.Precision = n;

                // 位数
                //field.Digit = GetDataRowValue<Int32>(dr, "NUMERIC_SCALE");
                if (TryGetDataRowValue<Int32>(dr, "NUMERIC_SCALE", out n))
                    field.Scale = n;
                else if (TryGetDataRowValue<Int32>(dr, "SCALE", out n))
                    field.Scale = n;

                // 长度
                if (TryGetDataRowValue<Int32>(dr, "CHARACTER_MAXIMUM_LENGTH", out n))
                    field.Length = n;
                else if (TryGetDataRowValue<Int32>(dr, "LENGTH", out n))
                    field.Length = n;
                else if (TryGetDataRowValue<Int32>(dr, "COLUMN_SIZE", out n))
                    field.Length = n;
                else
                    field.Length = field.Precision;

                // 字节数
                //field.NumOfByte = GetDataRowValue<Int32>(dr, "CHARACTER_OCTET_LENGTH");
                if (TryGetDataRowValue<Int32>(dr, "CHARACTER_OCTET_LENGTH", out n))
                    field.NumOfByte = n;
                else
                    field.NumOfByte = field.Length;

                // 允许空
                if (TryGetDataRowValue<Boolean>(dr, "IS_NULLABLE", out  b))
                    field.Nullable = b;
                else if (TryGetDataRowValue<String>(dr, "IS_NULLABLE", out  str))
                {
                    if (!String.IsNullOrEmpty(str)) field.Nullable = String.Equals("YES", str, StringComparison.OrdinalIgnoreCase);
                }
                else if (TryGetDataRowValue<String>(dr, "NULLABLE", out  str))
                {
                    if (!String.IsNullOrEmpty(str)) field.Nullable = String.Equals("Y", str, StringComparison.OrdinalIgnoreCase);
                }

                // 默认值
                field.Default = GetDataRowValue<String>(dr, "COLUMN_DEFAULT");

                // 描述
                field.Description = GetDataRowValue<String>(dr, "DESCRIPTION");

                FixField(field, dr);

                list.Add(field);
            }

            return list;
        }

        /// <summary>修正指定字段</summary>
        /// <param name="field"></param>
        /// <param name="dr"></param>
        protected virtual void FixField(IDataColumn field, DataRow dr)
        {
            ////String typeName = GetDataRowValue<String>(dr, "DATA_TYPE");
            //SetFieldType(field, field.RawType);

            DataTable dt = DataTypes;
            if (dt == null) return;

            DataRow[] drs = FindDataType(field, field.RawType, null);
            if (drs == null || drs.Length < 1)
                FixField(field, dr, null);
            else
                FixField(field, dr, drs[0]);
        }

        /// <summary>修正指定字段</summary>
        /// <param name="field">字段</param>
        /// <param name="drColumn">字段元数据</param>
        /// <param name="drDataType">字段匹配的数据类型</param>
        protected virtual void FixField(IDataColumn field, DataRow drColumn, DataRow drDataType)
        {
            String typeName = field.RawType;

            // 修正数据类型 +++重点+++
            if (TryGetDataRowValue<String>(drDataType, "DataType", out typeName))
            {
                field.DataType = TypeX.GetType(typeName);
            }

            // 修正长度为最大长度
            if (field.Length == 0)
            {
                Int32 n = 0;
                if (TryGetDataRowValue<Int32>(drDataType, "ColumnSize", out n))
                {
                    field.Length = n;
                    if (field.NumOfByte == 0) field.NumOfByte = field.Length;
                }

                if (field.Length <= 0 && field.DataType == typeof(String)) field.Length = Int32.MaxValue / 2;
            }

            // 处理格式参数
            if (!String.IsNullOrEmpty(field.RawType) && !field.RawType.EndsWith(")"))
            {
                String param = GetFormatParam(field, drDataType);
                if (!String.IsNullOrEmpty(param)) field.RawType += param;
            }
        }
        #endregion

        #region 索引架构
        DataTable _indexes;
        DataTable _indexColumns;
        /// <summary>获取索引</summary>
        /// <param name="table"></param>
        /// <returns></returns>
        protected virtual List<IDataIndex> GetIndexes(IDataTable table)
        {
            if (_indexes == null) return null;

            DataRow[] drs = _indexes.Select("table_name='" + table.Name + "'");
            if (drs == null || drs.Length < 1) return null;

            List<IDataIndex> list = new List<IDataIndex>();
            foreach (DataRow dr in drs)
            {
                String name = null;

                if (!TryGetDataRowValue<String>(dr, "INDEX_NAME", out name)) continue;

                IDataIndex di = table.CreateIndex();
                di.Name = name;

                if (TryGetDataRowValue<string>(dr, "COLUMN_NAME", out name) && !String.IsNullOrEmpty(name))
                    di.Columns = name.Split(new String[] { "," }, StringSplitOptions.RemoveEmptyEntries);
                else if (_indexColumns != null)
                {
                    DataRow[] dics = _indexColumns.Select("table_name='" + table.Name + "' And index_name='" + di.Name + "'", "ORDINAL_POSITION");
                    if (dics != null && dics.Length > 0)
                    {
                        List<String> ns = new List<string>();
                        foreach (DataRow item in dics)
                        {
                            String dcname = null;
                            if (TryGetDataRowValue<String>(item, "column_name", out dcname) &&
                                !String.IsNullOrEmpty(dcname) && !ns.Contains(dcname)) ns.Add(dcname);
                        }
                        if (ns.Count < 1) DAL.WriteDebugLog("表{0}的索引{1}无法取得字段列表！", table, di.Name);
                        di.Columns = ns.ToArray();
                    }
                }

                Boolean b = false;
                if (TryGetDataRowValue<Boolean>(dr, "UNIQUE", out b))
                    di.Unique = b;

                FixIndex(di, dr);

                list.Add(di);
            }
            return list != null && list.Count > 0 ? list : null;
        }

        /// <summary>修正索引</summary>
        /// <param name="index"></param>
        /// <param name="dr"></param>
        protected virtual void FixIndex(IDataIndex index, DataRow dr) { }
        #endregion

        #region 数据类型
        private DataTable _DataTypes;
        /// <summary>数据类型</summary>
        public DataTable DataTypes
        {
            get { return _DataTypes ?? (_DataTypes = GetSchema(DbMetaDataCollectionNames.DataTypes, null)); }
        }

        /// <summary>查找指定字段指定类型的数据类型</summary>
        /// <param name="field"></param>
        /// <param name="typeName"></param>
        /// <param name="isLong"></param>
        /// <returns></returns>
        protected virtual DataRow[] FindDataType(IDataColumn field, String typeName, Boolean? isLong)
        {
            if (String.IsNullOrEmpty(typeName)) throw new ArgumentNullException("typeName");
            // 去掉类型中，长度等限制条件
            if (typeName.Contains("(")) typeName = typeName.Substring(0, typeName.IndexOf("("));

            DataTable dt = DataTypes;
            if (dt == null) return null;

            DataRow[] drs = null;

            // 匹配TypeName，TypeName具有唯一性
            drs = dt.Select(String.Format("TypeName='{0}'", typeName));
            if (drs != null && drs.Length > 0)
            {
                //if (drs.Length > 1) throw new XDbMetaDataException(this, "TypeName具有唯一性！");
                return drs;
            }
            // 匹配DataType，重复的可能性很大
            DataRow[] drs2 = null;
            drs = dt.Select(String.Format("DataType='{0}'", typeName));
            if (drs != null && drs.Length > 0)
            {
                if (drs.Length == 1) return drs;

                StringBuilder sb = new StringBuilder();
                sb.AppendFormat("DataType='{0}' And ColumnSize>={1}", typeName, field.Length);
                //if (field.DataType == typeof(String) && field.Length > Database.LongTextLength) sb.AppendFormat(" And IsLong=1");
                // 如果字段的长度为0，则也算是大文本
                if (field.DataType == typeof(String) && (field.Length > Database.LongTextLength || field.Length <= 0))
                    sb.AppendFormat(" And IsLong=1");

                drs2 = dt.Select(sb.ToString(), "IsBestMatch Desc, ColumnSize Asc, IsFixedLength Asc, IsLong Asc");
                if (drs2 == null || drs2.Length < 1) return drs;
                if (drs2.Length == 1) return drs2;

                return drs2;
            }
            return null;
        }

        /// <summary>取字段类型</summary>
        /// <param name="field"></param>
        /// <returns></returns>
        protected virtual String GetFieldType(IDataColumn field)
        {
            String typeName = field.DataType.FullName;

            DataRow[] drs = FindDataType(field, typeName, null);
            if (drs == null || drs.Length < 1) return null;

            if (TryGetDataRowValue<String>(drs[0], "TypeName", out typeName))
            {
                // 处理格式参数
                String param = GetFormatParam(field, drs[0]);
                if (!String.IsNullOrEmpty(param) && param != "()") typeName += param;

                return typeName;
            }

            return null;
        }

        /// <summary>取得格式化的类型参数</summary>
        /// <param name="field"></param>
        /// <param name="dr"></param>
        /// <returns></returns>
        protected virtual String GetFormatParam(IDataColumn field, DataRow dr)
        {
            String ps = null;
            if (!TryGetDataRowValue<String>(dr, "CreateParameters", out ps) || String.IsNullOrEmpty(ps)) return null;

            String param = String.Empty;
            param += "(";
            String[] pms = ps.Split(new Char[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < pms.Length; i++)
            {
                Int32 item = GetFormatParamItem(field, dr, pms[i]);

                if (!param.EndsWith("(")) param += ",";
                param += item;
            }
            param += ")";

            return param;
        }

        /// <summary>获取格式化参数项</summary>
        /// <param name="field"></param>
        /// <param name="dr"></param>
        /// <param name="item"></param>
        /// <returns></returns>
        protected virtual Int32 GetFormatParamItem(IDataColumn field, DataRow dr, String item)
        {
            if (item.Contains("length") || item.Contains("size")) return field.Length;

            if (item.Contains("precision")) return field.Precision;

            if (item.Contains("scale") || item.Contains("bits"))
            {
                // 如果没有设置位数，则使用最大位数
                Int32 d = field.Scale;
                //if (d < 0)
                //{
                //    if (!TryGetDataRowValue<Int32>(dr, "MaximumScale", out d)) d = field.Scale;
                //}
                return d;
            }

            return 0;
        }
        #endregion
    }
}