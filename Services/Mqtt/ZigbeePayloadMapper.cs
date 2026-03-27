using AeroponicIOT.DTOs;
using System.Text.Json;

namespace AeroponicIOT.Services.Mqtt;

/// <summary>
/// Translates Zigbee2MQTT ZCL-derived JSON payloads into the internal
/// <see cref="SensorDataDto"/> format used by the sensor ingestion pipeline.
///
/// Zigbee2MQTT uses standard ZCL cluster attribute names (e.g. "temperature",
/// "humidity", "illuminance_lux") which differ from the ESP32 firmware JSON
/// keys ("waterTemperature", "airHumidity", "lightIntensity"). This mapper
/// normalises both naming conventions to a single DTO.
///
/// Non-standard ZCL fields (ph, tds) are also accepted for custom Zigbee
/// water-quality probes that expose them via manufacturer-specific clusters.
/// </summary>
internal static class ZigbeePayloadMapper
{
    // Maps every known ZCL / Zigbee2MQTT field name to the corresponding
    // SensorDataDto property setter.
    private static readonly Dictionary<string, Action<SensorDataDto, JsonElement>> FieldMap =
        new(StringComparer.OrdinalIgnoreCase)
        {
            // ── Temperature ───────────────────────────────────────────────
            // Standard Zigbee temperature cluster (0x0402) reports ambient /
            // air temperature. Mapped to WaterTemperature for now because that
            // is the only temperature field the schema currently has.
            ["temperature"]              = (d, e) => d.WaterTemperature = GetDouble(e),
            ["local_temperature"]        = (d, e) => d.WaterTemperature = GetDouble(e),
            ["water_temperature"]        = (d, e) => d.WaterTemperature = GetDouble(e),

            // ── Humidity ──────────────────────────────────────────────────
            ["humidity"]                 = (d, e) => d.AirHumidity = GetDouble(e),
            ["relative_humidity"]        = (d, e) => d.AirHumidity = GetDouble(e),
            ["soil_moisture"]            = (d, e) => d.AirHumidity = GetDouble(e),

            // ── Light ─────────────────────────────────────────────────────
            ["illuminance_lux"]          = (d, e) => d.LightIntensity = GetDouble(e),
            ["illuminance"]              = (d, e) => d.LightIntensity = GetDouble(e),
            ["light_intensity"]          = (d, e) => d.LightIntensity = GetDouble(e),

            // ── Water quality (custom / manufacturer-specific clusters) ───
            ["ph"]                       = (d, e) => d.Ph = GetDouble(e),
            ["tds"]                      = (d, e) => d.Tds = GetDouble(e),
            ["tds_ppb"]                  = (d, e) => d.Tds = GetDouble(e),
            ["electrical_conductivity"]  = (d, e) => d.Tds = GetDouble(e),
        };

    /// <summary>
    /// Attempts to map a Zigbee2MQTT device payload to a <see cref="SensorDataDto"/>.
    /// </summary>
    /// <param name="friendlyName">
    /// The Zigbee2MQTT friendly name (used as the device identifier when no
    /// IEEE address is available).
    /// </param>
    /// <param name="json">Raw JSON payload published by Zigbee2MQTT.</param>
    /// <returns>
    /// A populated <see cref="SensorDataDto"/> when at least one known sensor
    /// field was present; <c>null</c> otherwise (e.g. link-quality-only frames).
    /// </returns>
    public static SensorDataDto? TryMap(string friendlyName, string json)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json);
        }
        catch
        {
            return null;
        }

        using (doc)
        {
            var dto = new SensorDataDto { MacAddress = friendlyName };
            var matched = false;

            foreach (var property in doc.RootElement.EnumerateObject())
            {
                if (FieldMap.TryGetValue(property.Name, out var setter))
                {
                    setter(dto, property.Value);
                    matched = true;
                }
            }

            return matched ? dto : null;
        }
    }

    private static double? GetDouble(JsonElement el) => el.ValueKind switch
    {
        JsonValueKind.Number => el.GetDouble(),
        _                   => null,
    };
}
