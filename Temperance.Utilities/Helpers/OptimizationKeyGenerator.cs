namespace Temperance.Utilities.Helpers
{
    public class OptimizationKeyGenerator : IOptimizationKeyGenerator
    {
        public string GenerateOptimizationKey(string strategyName, string symbol, string interval, DateTime startDate, DateTime endDate)
        {
            var keyString = $"{strategyName}|{symbol}|{interval}|{startDate:O}|{endDate:O}";
            using var sha256 = System.Security.Cryptography.SHA256.Create();
            var bytes = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(keyString));
            return Convert.ToBase64String(bytes);
        }
    }
}
