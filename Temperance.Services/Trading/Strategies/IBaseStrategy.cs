namespace Temperance.Services.Trading.Strategies
{
    public interface IBaseStrategy
    {
        string Name { get; }

        void Initialize(double  initialCapital, Dictionary<string, object> parameters);
    }
}
