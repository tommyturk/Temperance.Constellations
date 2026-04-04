
namespace Temperance.Constellations.Utilities
{
    public static class PortfolioMath
    {
        /// <summary>
        /// Calculates the dynamic IDM based on the average correlation of the portfolio.
        /// IDM = N / sqrt(N + avgCorrelation * (N^2 - N))
        /// </summary>
        public static decimal CalculateDynamicIdm(List<decimal[]> assetReturnSeries)
        {
            int n = assetReturnSeries.Count;
            if (n <= 1) return 1.0m;

            decimal sumCorrelation = 0;
            int pairs = 0;

            // Calculate Pearson correlation for every unique pair
            for (int i = 0; i < n; i++)
            {
                for (int j = i + 1; j < n; j++)
                {
                    sumCorrelation += PearsonCorrelation(assetReturnSeries[i], assetReturnSeries[j]);
                    pairs++;
                }
            }

            decimal avgCorrelation = sumCorrelation / pairs;

            // We clamp at 0 because negative correlation technically increases IDM further,
            // but institutional risk limits usually cap the diversification benefit at 0.
            avgCorrelation = Math.Max(0, avgCorrelation);

            double idm = n / Math.Sqrt(n + (double)avgCorrelation * (n * n - n));
            return (decimal)idm;
        }

        private static decimal PearsonCorrelation(decimal[] x, decimal[] y)
        {
            if (x.Length != y.Length || x.Length == 0) return 0;

            decimal avgX = x.Average();
            decimal avgY = y.Average();

            decimal sumXx = 0, sumYy = 0, sumXy = 0;

            for (int i = 0; i < x.Length; i++)
            {
                decimal diffX = x[i] - avgX;
                decimal diffY = y[i] - avgY;

                sumXy += diffX * diffY;
                sumXx += diffX * diffX;
                sumYy += diffY * diffY;
            }

            if (sumXx == 0 || sumYy == 0) return 0;

            return sumXy / (decimal)Math.Sqrt((double)(sumXx * sumYy));
        }
    }
}
