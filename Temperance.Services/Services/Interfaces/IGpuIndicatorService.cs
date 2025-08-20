using ILGPU;

namespace Temperance.Services.Services.Interfaces
{
    public interface IGpuIndicatorService
    {
        double[] CalculateAtr(double[] high, double[] low, double[] close, int period);

        double[] CalculateSma(double[] prices, int period);

        double[] CalculateStdDev(double[] prices, int period);

        void Dispose();
    }
}
