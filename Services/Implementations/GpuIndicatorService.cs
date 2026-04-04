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
        private readonly Action<Index1D, ArrayView<double>, ArrayView<double>, int> _smaKernel;
        private readonly Action<Index1D, ArrayView<double>, ArrayView<double>, int> _stdDevKernel;
        private readonly Action<Index1D, ArrayView<double>, ArrayView<double>, ArrayView<double>, ArrayView<double>> _trKernel;
        private readonly Action<Index1D, ArrayView<double>, ArrayView<double>, int> _rsiKernel;
        private readonly ILogger<GpuIndicatorService> _logger;

        public GpuIndicatorService(ILogger<GpuIndicatorService> logger, Accelerator accelerator)
        {
            _accelerator = accelerator;
            _logger = logger;

            // LOAD KERNELS ONCE AT STARTUP
            _smaKernel = _accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<double>, ArrayView<double>, int>(SmaKernel);
            _stdDevKernel = _accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<double>, ArrayView<double>, int>(StdDevKernel);
            _trKernel = _accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<double>, ArrayView<double>, ArrayView<double>, ArrayView<double>>(TrueRangeKernel);
            _rsiKernel = _accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<double>, ArrayView<double>, int>(RsiKernel);

            _logger.LogInformation("GPU Kernels cached and ready for Warp Drive.");
        }

        // --- NEW BULK METHOD ---
        public async Task<Dictionary<DateTime, Dictionary<string, decimal>>> CalculateBulkIndicatorsAsync(
        ReadOnlyMemory<PriceModel> prices,
        Dictionary<string, object> parameters)
        {
            if (prices.IsEmpty) return new Dictionary<DateTime, Dictionary<string, decimal>>();

            // 1. FAST PARSING
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
                    try { return (T)Convert.ChangeType(val, typeof(T)); } catch { }
                }
                return defaultValue;
            }

            int maPeriod = Parse(20, "MovingAveragePeriod", "MaPeriod");
            int maLongPeriod = 1300;
            double stdDevMult = Parse(2.0, "StdDevMultiplier");
            int rsiPeriod = Parse(14, "RSIPeriod", "RsiPeriod");
            int atrPeriod = Parse(14, "AtrPeriod");

            int n = prices.Length;

            // 2. HIGH PERFORMANCE DATA COPY (Avoids LINQ/Select overhead)
            var highD = new double[n];
            var lowD = new double[n];
            var closeD = new double[n];
            var timestamps = new DateTime[n];

            var span = prices.Span;
            
            for (int i = 0; i < n; i++)
            {
                var p = span[i];
                highD[i] = (double)p.HighPrice;
                lowD[i] = (double)p.LowPrice;
                closeD[i] = (double)p.ClosePrice;
                timestamps[i] = p.Timestamp;
            }

            // 3. GPU ALLOCATIONS
            using var closeBuffer = _accelerator.Allocate1D(closeD);
            using var highBuffer = _accelerator.Allocate1D(highD);
            using var lowBuffer = _accelerator.Allocate1D(lowD);
            using var smaBuffer = _accelerator.Allocate1D<double>(n);
            using var smaLongBuffer = _accelerator.Allocate1D<double>(n);
            using var stdDevBuffer = _accelerator.Allocate1D<double>(n);
            using var trBuffer = _accelerator.Allocate1D<double>(n);
            using var rsiBuffer = _accelerator.Allocate1D<double>(n);

            // 4. EXECUTION (Using cached delegates)
            _smaKernel(n, closeBuffer.View, smaBuffer.View, maPeriod);
            _smaKernel(n, closeBuffer.View, smaLongBuffer.View, maLongPeriod); // Now actually running SMA_Long
            _stdDevKernel(n, closeBuffer.View, stdDevBuffer.View, maPeriod);
            _trKernel(n, highBuffer.View, lowBuffer.View, closeBuffer.View, trBuffer.View);
            _rsiKernel(n, closeBuffer.View, rsiBuffer.View, rsiPeriod);

            _accelerator.Synchronize();

            // 5. DATA RETRIEVAL
            var sma = smaBuffer.GetAsArray1D();
            var smaLong = smaLongBuffer.GetAsArray1D();
            var stdDev = stdDevBuffer.GetAsArray1D();
            var tr = trBuffer.GetAsArray1D();
            var rsi = rsiBuffer.GetAsArray1D();
            var atr = CalculateAtrSmoothing(tr, atrPeriod);

            // 6. RESULT MAPPING
            var results = new Dictionary<DateTime, Dictionary<string, decimal>>(n);
            for (int i = 0; i < n; i++)
            {
                // Grab previous values safely
                decimal rsiPrev = i > 0 ? (decimal)rsi[i - 1] : 50m;
                decimal closePrev = i > 0 ? (decimal)closeD[i - 1] : (decimal)closeD[i];

                results[timestamps[i]] = new Dictionary<string, decimal>(8)
                {
                    { "SMA", (decimal)sma[i] },
                    { "SMA_Long", (decimal)smaLong[i] },
                    { "RSI", (decimal)rsi[i] },
                    { "RSI_Prev", rsiPrev },             
                    { "Close_Prev", closePrev },         
                    { "ATR", (decimal)atr[i] },
                    { "UpperBand", (decimal)(sma[i] + (stdDevMult * stdDev[i])) },
                    { "LowerBand", (decimal)(sma[i] - (stdDevMult * stdDev[i])) }
                };
                        }

            return results;
        }

        public async Task<Dictionary<DateTime, decimal>> CalculateSmaOnlyAsync(
            ReadOnlyMemory<PriceModel> prices,
            int period)
        {
            if (prices.IsEmpty) return new();

            int n = prices.Length;
            var closeD = new double[n];
            var timestamps = new DateTime[n];

            var span = prices.Span;
            // Fast copy
            for (int i = 0; i < n; i++)
            {
                closeD[i] = (double)span[i].ClosePrice;
                timestamps[i] = span[i].Timestamp;
            }

            using var closeBuffer = _accelerator.Allocate1D(closeD);
            using var smaBuffer = _accelerator.Allocate1D<double>(n);

            // Use the cached SMA kernel from the constructor
            _smaKernel(n, closeBuffer.View, smaBuffer.View, period);
            _accelerator.Synchronize();

            var smaResults = smaBuffer.GetAsArray1D();

            var results = new Dictionary<DateTime, decimal>(n);
            for (int i = 0; i < n; i++)
            {
                results[timestamps[i]] = (decimal)smaResults[i];
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

        public void Dispose() {; }
    }
}