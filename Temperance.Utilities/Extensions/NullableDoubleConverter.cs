using Newtonsoft.Json;

namespace Temperance.Utilities.Extensions
{
    public class NullableDoubleConverter : JsonConverter<double?>
    {
        public override void WriteJson(JsonWriter writer, double? value, JsonSerializer serializer)
        {
            writer.WriteValue(value);
        }

        public override double? ReadJson(JsonReader reader, Type objectType, double? existingValue, bool hasExistingValue, JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.Null)
            {
                return null; // Return null if the token is null
            }

            var value = reader.Value?.ToString();

            if (string.IsNullOrEmpty(value) || value == "-")
            {
                return null; // Return null if the value is "-" or empty
            }

            if (double.TryParse(value, out double result))
            {
                return result;
            }

            return null;
        }

    }
}
