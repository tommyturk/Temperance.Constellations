namespace Temperance.Utilities.Common
{
    public interface IStatusUpdateSender
    {
        Task SendStatusUpdateAsync(string message, string symbol = null, string interval = null);
    }
}
