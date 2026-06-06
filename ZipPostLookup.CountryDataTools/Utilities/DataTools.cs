using System.Data;
using static Dapper.SqlMapper;

namespace ZipPostLookup.CountryDataTools.Utilities
{
    public static class DataTools
    {
        public static DataTable ConvertArrayToCodeDataTable(string[] ids)
        {
            var result = new DataTable();

            result.Columns.Add("Code", typeof(string));

            foreach (var id in ids) 
            {
                result.Rows.Add(id);
            }

            return result;
        }

        public static ICustomQueryParameter ConvertArrayToCodeTableParameter(string[] ids)
        {
            var stringIdDataTable = ConvertArrayToCodeDataTable(ids);

            var result = stringIdDataTable.AsTableValuedParameter("Utilities.Code");

            return result;
        }
    }
}
