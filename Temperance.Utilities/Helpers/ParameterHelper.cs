using System.Globalization;
using System.Text.Json;

namespace Temperance.Utilities.Helpers
{
    public static class ParameterHelper
    {
        public static T GetParameterOrDefault<T>(Dictionary<string, object> parameters, string key, T defaultValue)
        {
            if (!parameters.TryGetValue(key, out var value) || value == null)
            {
                return defaultValue;
            }

            try
            {
                if (value is JsonElement jsonElement)
                {
                    return ConvertJsonElement<T>(jsonElement, defaultValue);
                }

                // Fallback for non-JsonElement values
                return (T)Convert.ChangeType(value, typeof(T), CultureInfo.InvariantCulture);
            }
            catch (Exception ex) when (ex is InvalidCastException || ex is FormatException || ex is OverflowException || ex is InvalidOperationException)
            {
                // If any conversion fails, return the default value.
                return defaultValue;
            }
        }

        private static T ConvertJsonElement<T>(JsonElement jsonElement, T defaultValue)
        {
            Type targetType = typeof(T);

            if (targetType == typeof(int))
            {
                if (jsonElement.TryGetInt32(out var intValue))
                    return (T)(object)intValue;
                // If it's a double without a decimal part, convert it.
                if (jsonElement.TryGetDouble(out var doubleValue))
                    return (T)(object)Convert.ToInt32(doubleValue);
            }
            else if (targetType == typeof(double))
            {
                if (jsonElement.TryGetDouble(out var doubleValue))
                    return (T)(object)doubleValue;
                // If it's an int, convert it.
                if (jsonElement.TryGetInt32(out var intValue))
                    return (T)(object)Convert.ToDouble(intValue);
            }
            else if (targetType == typeof(decimal))
            {
                if (jsonElement.TryGetDecimal(out var decimalValue))
                    return (T)(object)decimalValue;
                // If it's a double, convert it.
                if (jsonElement.TryGetDouble(out var doubleValue))
                    return (T)(object)Convert.ToDecimal(doubleValue);
            }
            else if (targetType == typeof(string))
            {
                return (T)(object)jsonElement.ToString();
            }

            // Fallback for other types or if primary conversions fail
            return JsonSerializer.Deserialize<T>(jsonElement.GetRawText()) ?? defaultValue;
        }
    }
}