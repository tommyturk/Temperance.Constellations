namespace Temperance.Utilities.Helpers
{
    public interface ISqlHelper
    {
        Task<bool> TableExists(string tableName);

        public string SanitizeTableName(string symbl, string interval);

        Task EnsureTableExists(string tableName);
    }
}
