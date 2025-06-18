using System.Text.Json;

namespace Temperance.Utilities.Helpers
{
    public static class ParameterHelper
    {
        public static T GetParameterOrDefault<T>(Dictionary<string, object> parameters, string key, T defaultValue)
        {
            if (!parameters.TryGetValue(key, out var value))
                return defaultValue;

            try
            {
                if (value is JsonElement jsonElement)
                {
                    if (typeof(T) == typeof(decimal))
                    {
                        if (jsonElement.TryGetDecimal(out var decimalValue))
                            return (T)(object)decimalValue;
                    }
                    if (typeof(T) == typeof(int))
                    {
                        if (jsonElement.TryGetInt32(out var intValue))
                            return (T)(object)intValue;
                    }
                    if (typeof(T) == typeof(double))
                    {
                        if (jsonElement.TryGetDouble(out var doubleValue))
                            return (T)(object)doubleValue;
                    }
                    return (T)Convert.ChangeType(jsonElement.ToString(), typeof(T));
                }

                return (T)Convert.ChangeType(value, typeof(T));
            }
            catch (Exception ex) when (ex is InvalidCastException || ex is FormatException || ex is OverflowException)
            {
                return defaultValue;
            }
        }
    }
}
