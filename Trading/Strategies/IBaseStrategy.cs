namespace Temperance.Services.Trading.Strategies
{
    public interface IBaseStrategy
    {
        string Name { get; }

        void Initialize(decimal initialCapital, Dictionary<string, object> parameters);
    }
}
