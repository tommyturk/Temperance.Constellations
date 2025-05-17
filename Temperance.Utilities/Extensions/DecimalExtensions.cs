using Newtonsoft.Json;
using System;
namespace Temperance.Utilities.Extensions
{
    public static class DecimalExtensions
    {
        public static decimal? ConvertToDecimal(this string value)
        {
            if (string.Equals(value, "None", StringComparison.OrdinalIgnoreCase))
            {
                return 0.0m;
            }

            if (decimal.TryParse(value, out var result))
            {
                return result;
            }

            return null;
        }
    }

    public class DecimalConverter : JsonConverter<decimal>
    {
        public override decimal ReadJson(JsonReader reader, Type objectType, decimal existingValue, bool hasExistingValue, JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.String)
            {
                var value = (string)reader.Value;
                return value.ConvertToDecimal() ?? 0.0m;
            }
            return Convert.ToDecimal(reader.Value);
        }

        public override void WriteJson(JsonWriter writer, decimal value, JsonSerializer serializer)
        {
            writer.WriteValue(value);
        }
    }

    public class NullableDecimalConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType)
        {
            return objectType == typeof(decimal?) || objectType == typeof(decimal);
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.Null)
                return null;

            if (reader.TokenType == JsonToken.String)
            {
                var stringValue = reader.Value.ToString();
                if (stringValue == "None")
                    return null;
            }

            return Convert.ToDecimal(reader.Value);
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            writer.WriteValue(value);
        }
    }
}
