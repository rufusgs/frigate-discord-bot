using System.Text.Json;
using System.Text.Json.Serialization;

namespace FrigateBot
{
    public sealed class EpochTimeJsonConverter : JsonConverter<DateTimeOffset>
    {
        public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var secondsSinceEpoch = reader.GetDouble();
            var result = new DateTimeOffset((long)(secondsSinceEpoch * 10000000) + DateTimeOffset.UnixEpoch.Ticks, TimeSpan.Zero);
            return result;
        }

        public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options)
        {
            writer.WriteNumberValue(ToEpochTime(value));
        }

        public static double ToEpochTime(DateTimeOffset value) => (double)(value.UtcTicks - DateTimeOffset.UnixEpoch.Ticks) / 10000000;
    }
}
