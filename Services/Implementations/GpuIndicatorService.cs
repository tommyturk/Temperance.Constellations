using ILGPU;
using ILGPU.Algorithms;
using ILGPU.Runtime;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using Temperance.Constellations.Services.Interfaces;
using Temperance.Ephemeris.Models.Prices;

namespace Temperance.Services.Services.Implementations
{
    public class GpuIndicatorService : IGpuIndicatorService, IDisposable
    {
        private readonly Accelerator _accelerator;
        private readonly ILogger<GpuIndicatorService> _logger;

        public GpuIndicatorService(ILogger<GpuIndicatorService> logger, Accelerator accelerator)
        {
            _accelerator = accelerator;
            _logger = logger;
        }

        // --- NEW BULK METHOD ---
        public async Task<Dictionary<DateTime, Dictionary<string, decimal>>> CalculateBulkIndicatorsAsync(
            List<PriceModel> prices,
            Dictionary<string, object> parameters)
        {
            if (prices == null || prices.Count == 0) return new();

            // The "Swiss Army Knife" of parsers
            T Parse<T>(T defaultValue, params string[] keys)
            {
                foreach (var key in keys)
                {
                    if (!parameters.TryGetValue(key, out var val) || val == null) continue;

                    if (val is System.Text.Json.JsonElement element)
                    {
                        if (typeof(T) == typeof(int)) return (T)(object)element.GetInt32();
                        if (typeof(T) == typeof(double)) return (T)(object)element.GetDouble();
                        if (typeof(T) == typeof(decimal)) return (T)(object)element.GetDecimal();
                    }

                    try { return (T)Convert.ChangeType(val, typeof(T)); }
                    catch { /* try next key */ }
                }
                return defaultValue;
            }

            // --- MAPPING YOUR CPO KEYS TO GPU VARIABLES ---

            // MaPeriod: checks "MovingAveragePeriod" then falls back to "MaPeriod"
            int maPeriod = Parse(20, "MovingAveragePeriod", "MaPeriod");

            // StdDevMult: checks "StdDevMultiplier"
            double stdDevMult = Parse(2.0, "StdDevMultiplier");

            // RsiPeriod: checks "RSIPeriod" (Note the casing!)
            int rsiPeriod = Parse(14, "RSIPeriod", "RsiPeriod");

            // AtrPeriod: checks "AtrPeriod"
            int atrPeriod = Parse(14, "AtrPeriod");

            // Other params from your list (in case you need them later)
            double rsiOversold = Parse(30.0, "RSIOversold");
            double rsiOverbought = Parse(70.0, "RSIOverbought");
            double atrMult = Parse(2.5, "AtrMultiplier");
            int n = prices.Count;
            var timestamps = prices.Select(p => p.Timestamp).ToList();

            var highD = prices.Select(p => (double)p.HighPrice).ToArray();
            var lowD = prices.Select(p => (double)p.LowPrice).ToArray();
            var closeD = prices.Select(p => (double)p.ClosePrice).ToArray();

            using var closeBuffer = _accelerator.Allocate1D(closeD);
            using var highBuffer = _accelerator.Allocate1D(highD);
            using var lowBuffer = _accelerator.Allocate1D(lowD);
            using var smaBuffer = _accelerator.Allocate1D<double>(n);
            using var stdDevBuffer = _accelerator.Allocate1D<double>(n);
            using var trBuffer = _accelerator.Allocate1D<double>(n);
            using var rsiBuffer = _accelerator.Allocate1D<double>(n);

            var smaKernel = _accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<double>, ArrayView<double>, int>(SmaKernel);
            var stdDevKernel = _accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<double>, ArrayView<double>, int>(StdDevKernel);
            var trKernel = _accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<double>, ArrayView<double>, ArrayView<double>, ArrayView<double>>(TrueRangeKernel);
            var rsiKernel = _accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<double>, ArrayView<double>, int>(RsiKernel);

            smaKernel(n, closeBuffer.View, smaBuffer.View, maPeriod);
            stdDevKernel(n, closeBuffer.View, stdDevBuffer.View, maPeriod);
            trKernel(n, highBuffer.View, lowBuffer.View, closeBuffer.View, trBuffer.View);
            rsiKernel(n, closeBuffer.View, rsiBuffer.View, rsiPeriod);

            _accelerator.Synchronize();

            var sma = smaBuffer.GetAsArray1D();
            var stdDev = stdDevBuffer.GetAsArray1D();
            var tr = trBuffer.GetAsArray1D();
            var rsi = rsiBuffer.GetAsArray1D();
            var atr = CalculateAtrSmoothing(tr, atrPeriod);

            var results = new Dictionary<DateTime, Dictionary<string, decimal>>();
            for (int i = 0; i < n; i++)
            {
                results[timestamps[i]] = new Dictionary<string, decimal>
                {
                    { "SMA", (decimal)sma[i] },
                    { "RSI", (decimal)rsi[i] },
                    { "ATR", (decimal)atr[i] },
                    { "UpperBand", (decimal)(sma[i] + (stdDevMult * stdDev[i])) },
                    { "LowerBand", (decimal)(sma[i] - (stdDevMult * stdDev[i])) }
                };
            }
            return results;
        }

        // --- INTERFACE MANDATED METHODS (NOW PUBLIC) ---

        public decimal[] CalculateSma(decimal[] prices, int period)
        {
            var pricesD = Array.ConvertAll(prices, p => (double)p);
            using var pBuffer = _accelerator.Allocate1D(pricesD);
            using var oBuffer = _accelerator.Allocate1D<double>(prices.Length);
            var kernel = _accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<double>, ArrayView<double>, int>(SmaKernel);
            kernel((int)prices.Length, pBuffer.View, oBuffer.View, period);
            _accelerator.Synchronize();
            return Array.ConvertAll(oBuffer.GetAsArray1D(), d => (decimal)d);
        }

        public decimal[] CalculateStdDev(decimal[] prices, int period)
        {
            var pricesD = Array.ConvertAll(prices, p => (double)p);
            using var pBuffer = _accelerator.Allocate1D(pricesD);
            using var oBuffer = _accelerator.Allocate1D<double>(prices.Length);
            var kernel = _accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<double>, ArrayView<double>, int>(StdDevKernel);
            kernel((int)prices.Length, pBuffer.View, oBuffer.View, period);
            _accelerator.Synchronize();
            return Array.ConvertAll(oBuffer.GetAsArray1D(), d => (decimal)d);
        }

        public decimal[] CalculateAtr(decimal[] high, decimal[] low, decimal[] close, int period)
        {
            var hD = Array.ConvertAll(high, p => (double)p);
            var lD = Array.ConvertAll(low, p => (double)p);
            var cD = Array.ConvertAll(close, p => (double)p);
            using var hB = _accelerator.Allocate1D(hD);
            using var lB = _accelerator.Allocate1D(lD);
            using var cB = _accelerator.Allocate1D(cD);
            using var trB = _accelerator.Allocate1D<double>(high.Length);

            var kernel = _accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<double>, ArrayView<double>, ArrayView<double>, ArrayView<double>>(TrueRangeKernel);
            kernel((int)high.Length, hB.View, lB.View, cB.View, trB.View);
            _accelerator.Synchronize();

            return Array.ConvertAll(CalculateAtrSmoothing(trB.GetAsArray1D(), period), d => (decimal)d);
        }

        // --- INTERNAL LOGIC & KERNELS ---

        private double[] CalculateAtrSmoothing(double[] trueRanges, int period)
        {
            int n = trueRanges.Length;
            double[] atr = new double[n];
            if (n <= period) return atr; 
            double sum = 0;
            for (int i = 1; i <= period; i++) sum += trueRanges[i];
            atr[period] = sum / period;
            for (int i = period + 1; i < n; i++) atr[i] = (atr[i - 1] * (period - 1) + trueRanges[i]) / period;
            return atr;
        }

        private static void SmaKernel(Index1D index, ArrayView<double> prices, ArrayView<double> output, int period)
        {
            if (index < period - 1) return;
            double sum = 0;
            for (int i = 0; i < period; i++) sum += prices[index - i];
            output[index] = sum / period;
        }

        private static void StdDevKernel(Index1D index, ArrayView<double> prices, ArrayView<double> output, int period)
        {
            if (index < period - 1) return;
            double sum = 0;
            for (int i = 0; i < period; i++) sum += prices[index - i];
            double mean = sum / period;
            double sumSq = 0;
            for (int i = 0; i < period; i++)
            {
                double diff = prices[index - i] - mean;
                sumSq += diff * diff;
            }
            output[index] = XMath.Sqrt(sumSq / (period - 1));
        }

        private static void RsiKernel(Index1D index, ArrayView<double> prices, ArrayView<double> output, int period)
        {
            if (index < period) return;
            double gains = 0, losses = 0;
            for (int i = 0; i < period; i++)
            {
                double diff = prices[index - i] - prices[index - i - 1];
                if (diff > 0) gains += diff; else losses -= diff;
            }
            output[index] = (gains + losses == 0) ? 50 : 100 * (gains / period) / ((gains / period) + (losses / period));
        }

        private static void TrueRangeKernel(Index1D index, ArrayView<double> high, ArrayView<double> low, ArrayView<double> close, ArrayView<double> output)
        {
            if (index == 0) { output[index] = 0; return; }
            output[index] = XMath.Max(high[index] - low[index],
                           XMath.Max(XMath.Abs(high[index] - close[index - 1]),
                                     XMath.Abs(low[index] - close[index - 1])));
        }

        public void Dispose() { _accelerator?.Dispose(); }
    }
}