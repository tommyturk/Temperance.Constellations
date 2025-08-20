using ILGPU;
using ILGPU.Algorithms;
using ILGPU.Runtime;
using ILGPU.Runtime.Cuda;
using Microsoft.Extensions.Logging;
using Temperance.Services.Services.Interfaces;
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

        public double[] CalculateAtr(double[] high, double[] low, double[] close, int period)
        {
            if (_accelerator == null) throw new InvalidOperationException("GPU Accelerator not found");
            if (high.Length <= period) return new double[high.Length];

            using var highBuffer = _accelerator.Allocate1D(high);
            using var lowBuffer = _accelerator.Allocate1D(low);
            using var closeBuffer = _accelerator.Allocate1D(close);
            using var trueRangeBuffer = _accelerator.Allocate1D<double>(high.Length);

            var loadedKernel = _accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<double>, ArrayView<double>, ArrayView<double>, ArrayView<double>>(TrueRangeKernel);
            loadedKernel(high.Length, highBuffer.View, lowBuffer.View, closeBuffer.View, trueRangeBuffer.View);
            _accelerator.Synchronize();

            var trueRanges = trueRangeBuffer.GetAsArray1D();

            var atr = new double[high.Length];

            double initialAtrSum = 0.0;
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
                                    ArrayView<double> high,
                                    ArrayView<double> low,
                                    ArrayView<double> close,
                                    ArrayView<double> output)
        {
            if (index == 0)
            {
                output[index] = 0;
                return;
            }
            double highLow = high[index] - low[index];
            double highPrevClose = XMath.Abs(high[index] - close[index - 1]);
            double lowPrevClose = XMath.Abs(low[index] - close[index - 1]);
            output[index] = XMath.Max(highLow, XMath.Max(highPrevClose, lowPrevClose));
        }

        public double[] CalculateSma(double[] prices, int period)
        {
            var output = new double[prices.Length];

            using var priceBuffer = _accelerator.Allocate1D(prices);
            using var outputBuffer = _accelerator.Allocate1D<double>(prices.Length);

            var loadedKernel = _accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<double>, ArrayView<double>, int>(SmaKernel);

            loadedKernel(prices.Length, priceBuffer.View, outputBuffer.View, period);

            _accelerator.Synchronize();
            return outputBuffer.GetAsArray1D();
        }

        public double[] CalculateStdDev(double[] prices, int period)
        {
            if (_accelerator == null) throw new InvalidOperationException("GPU accelerator is not initialized.");
            if (prices == null || prices.Length < period) return new double[prices.Length];

            var pricesAsDouble = Array.ConvertAll(prices, p => (double)p);

            using var priceBuffer = _accelerator.Allocate1D(pricesAsDouble);
            using var outputBuffer = _accelerator.Allocate1D<double>(prices.Length);

            var loadedKernel = _accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<double>, ArrayView<double>, int>(StdDevKernel);
            loadedKernel(prices.Length, priceBuffer.View, outputBuffer.View, period);
            _accelerator.Synchronize();
            var resultAsDouble = outputBuffer.GetAsArray1D();
            return Array.ConvertAll(resultAsDouble, d => (double)d);
        }

        

        private static void StdDevKernel(Index1D index, ArrayView<double> prices, ArrayView<double> output, int period)
        {
            if (index < period - 1)
            {
                output[index] = 0;
                return;
            }
            double sum = 0.0;
            for (int i = 0; i < period; i++)
            {
                sum += prices[index - i];
            }
            double mean = sum / period;
            double sumOfSquares = 0.0;
            for (int i = 0; i < period; i++)
            {
                double deviation = prices[index - i] - mean;
                sumOfSquares += deviation * deviation;
            }

            double variance = sumOfSquares / (period - 1);
            output[index] = Math.Sqrt(variance);
        }

        private static void SmaKernel(Index1D index, ArrayView<double> prices, ArrayView<double> output, int period)
        {
            if (index < period - 1) return;

            double sum = 0;
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

        public decimal[] CalculateSma(decimal[] prices, int period)
        {
            throw new NotImplementedException();
        }
    }
}
