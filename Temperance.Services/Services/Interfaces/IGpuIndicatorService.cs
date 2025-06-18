using ILGPU;

namespace Temperance.Services.Services.Interfaces
{
    public interface IGpuIndicatorService
    {
        double[] CalculateSma(double[] prices, int period);

        double[] CalculateStdDev(double[] prices, int period);

        void Dispose();
    }
}
