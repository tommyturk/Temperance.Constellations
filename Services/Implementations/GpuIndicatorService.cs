using ILGPU;
using ILGPU.Algorithms;
using ILGPU.Runtime;
using ILGPU.Runtime.Cuda;
using Temperance.Ephemeris.Models.Prices;
using Temperance.Constellations.Services.Interfaces;
namespace Temperance.Services.Services.Implementations
{
    public class GpuIndicatorService : IGpuIndicatorService
    {
        private readonly Context _context;
        private readonly Accelerator _accelerator;
        private readonly ILogger<GpuIndicatorService> _logger;
        public GpuIndicatorService(ILogger<GpuIndicatorService> logger, Accelerator accelerator)
        {
            _context = Context.Create(builder => builder.Cuda());
            //_accelerator = _context.GetPreferredDevice(preferCPU: false).CreateAccelerator(_context);
            _accelerator = accelerator;
            _logger = logger;
        }

        public async Task<Dictionary<string, decimal[]>> CalculateIndicatorsAsync(IReadOnlyList<PriceModel> historicalWindow, 
            int strategyMinimumLookback, int atrPeriod, decimal stdDevMultiplier, decimal[] rsi)
        {
            var highPrices = historicalWindow.Select(p => p.HighPrice).ToArray();
            var lowPrices = historicalWindow.Select(p => p.LowPrice).ToArray();
            var closePrices = historicalWindow.Select(p => p.ClosePrice).ToArray();

            var movingAverage = CalculateSma(closePrices, strategyMinimumLookback);
            var standardDeviation = CalculateStdDev(closePrices, strategyMinimumLookback);
            var atr = CalculateAtr(highPrices, lowPrices, closePrices, atrPeriod);
            var upperBand = movingAverage.Zip(standardDeviation, (m, s) => m + (stdDevMultiplier * s)).ToArray();  
            var lowerBand = movingAverage.Zip(standardDeviation, (m, s) => m - (stdDevMultiplier * s)).ToArray();

            return new Dictionary<string, decimal[]>
            {
                { "SMA", movingAverage }, { "ATR", atr }, { "UpperBand", upperBand }, { "LowerBand", lowerBand },
                { "RSI", rsi }
            };
        }

        public decimal[] CalculateAtr(decimal[] high, decimal[] low, decimal[] close, int period)
        {
            if (_accelerator == null) throw new InvalidOperationException("GPU Accelerator not found");
            if (high.Length <= period) return new decimal[high.Length];

            using var highBuffer = _accelerator.Allocate1D(high);
            using var lowBuffer = _accelerator.Allocate1D(low);
            using var closeBuffer = _accelerator.Allocate1D(close);
            using var trueRangeBuffer = _accelerator.Allocate1D<decimal>(high.Length);

            var loadedKernel = _accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<decimal>, ArrayView<decimal>, ArrayView<decimal>, ArrayView<decimal>>(TrueRangeKernel);
            loadedKernel(high.Length, highBuffer.View, lowBuffer.View, closeBuffer.View, trueRangeBuffer.View);
            _accelerator.Synchronize();

            var trueRanges = trueRangeBuffer.GetAsArray1D();

            var atr = new decimal[high.Length];

            decimal initialAtrSum = 0.0m;
            for (int i = 1; i <= period; i++)
            {
                initialAtrSum += trueRanges[i];
            }
            atr[period] = initialAtrSum / period;

            for (int i = period + 1; i < high.Length; i++)
            {
                atr[i] = ((atr[i - 1] * (period - 1)) + trueRanges[i]) / period;
            }

            return atr;
        }

        private static void TrueRangeKernel(Index1D index,
                                    ArrayView<decimal> high,
                                    ArrayView<decimal> low,
                                    ArrayView<decimal> close,
                                    ArrayView<decimal> output)
        {
            if (index == 0)
            {
                output[index] = 0;
                return;
            }
            double highLow = (double)(high[index] - low[index]);
            double highPrevClose = XMath.Abs((double)(high[index] - close[index - 1]));
            double lowPrevClose = XMath.Abs((double)(low[index] - close[index - 1]));
            output[index] = (decimal)XMath.Max(highLow, XMath.Max(highPrevClose, lowPrevClose));
        }

        public decimal[] CalculateSma(decimal[] prices, int period)
        {
            var output = new decimal[prices.Length];

            using var priceBuffer = _accelerator.Allocate1D(prices);
            using var outputBuffer = _accelerator.Allocate1D<decimal>(prices.Length);

            var loadedKernel = _accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<decimal>, ArrayView<decimal>, int>(SmaKernel);

            loadedKernel(prices.Length, priceBuffer.View, outputBuffer.View, period);

            _accelerator.Synchronize();
            return outputBuffer.GetAsArray1D();
        }

        public decimal[] CalculateStdDev(decimal[] prices, int period)
        {
            if (_accelerator == null) throw new InvalidOperationException("GPU accelerator is not initialized.");
            if (prices == null || prices.Length < period) return new decimal[prices.Length];

            var pricesAsDouble = Array.ConvertAll(prices, p => (decimal)p);

            using var priceBuffer = _accelerator.Allocate1D(pricesAsDouble);
            using var outputBuffer = _accelerator.Allocate1D<decimal>(prices.Length);

            var loadedKernel = _accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<decimal>, ArrayView<decimal>, int>(StdDevKernel);
            loadedKernel(prices.Length, priceBuffer.View, outputBuffer.View, period);
            _accelerator.Synchronize();
            var resultAsDouble = outputBuffer.GetAsArray1D();
            return Array.ConvertAll(resultAsDouble, d => (decimal)d);
        }

        

        private static void StdDevKernel(Index1D index, ArrayView<decimal> prices, ArrayView<decimal> output, int period)
        {
            if (index < period - 1)
            {
                output[index] = 0;
                return;
            }
            decimal sum = 0.0m;
            for (int i = 0; i < period; i++)
            {
                sum += prices[index - i];
            }
            decimal mean = sum / period;
            decimal sumOfSquares = 0.0m;
            for (int i = 0; i < period; i++)
            {
                decimal deviation = prices[index - i] - mean;
                sumOfSquares += deviation * deviation;
            }

            decimal variance = sumOfSquares / (period - 1);

            output[index] = (decimal)Math.Sqrt((double)variance);
        }

        private static void SmaKernel(Index1D index, ArrayView<decimal> prices, ArrayView<decimal> output, int period)
        {
            if (index < period - 1) return;

            decimal sum = 0;
            for (int i = 0; i < period; i++)
            {
                sum += prices[index - i];
            }
            output[index] = sum / period;
        }

        public void Dispose()
        {
            _accelerator.Dispose();
            _context.Dispose();
        }
    }
}
