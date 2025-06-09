namespace Temperance.Services.Trading.Strategies
{
    public interface IBaseStrategy
    {
        string Name { get; }

        void Initialize(decimal nitialCapital, Dictionary<string, object> parameters);
    }
}
