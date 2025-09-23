using System.Security.Cryptography;
using System.Text;

namespace Temperance.Utilities.Helpers
{
    public class OptimizationKeyGenerator : IOptimizationKeyGenerator
    {
        public string GenerateOptimizationKey(string strategyName, string symbol, string interval, DateTime startDate, DateTime endDate)
        {
            string keyString = $"{strategyName}_{symbol}_{interval}_{startDate:yyyy-MM-dd}_{endDate:yyyy-MM-dd}";

            using (SHA256 sha256 = SHA256.Create())
            {
                byte[] bytes = Encoding.UTF8.GetBytes(keyString);
                byte[] hashBytes = sha256.ComputeHash(bytes);
                return Convert.ToBase64String(hashBytes);
            }
        }
    }
}
