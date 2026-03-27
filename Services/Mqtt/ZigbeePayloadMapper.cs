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
/// When Zigbee2MQTT is configured with
/// <c>advanced.include_device_information: true</c> every telemetry message
/// contains an <c>ieee_address</c> field (e.g. "0x00124b0014d9b021") which is
/// the stable hardware EUI-64 identifier. The mapper uses it as the canonical
/// <see cref="SensorDataDto.MacAddress"/> value (uppercased) so lookups against
/// Device.MacAddress stored during auto-provisioning succeed. When the field is
/// absent the Zigbee2MQTT friendly name is used as a fallback.
///
/// Non-standard ZCL fields (ph, tds) are also accepted for custom Zigbee
/// water-quality probes that expose them via manufacturer-specific clusters.
/// </summary>
internal static class ZigbeePayloadMapper
{
    // Maps every known ZCL / Zigbee2MQTT field name to the corresponding
    // SensorDataDto property setter.
    // NOTE: illuminance (raw 16-bit integer) is intentionally kept separate
    // from illuminance_lux (calculated). The mapper prefers the lux value;
    // the raw value is only applied when no lux reading is present – see TryMap.
    private static readonly Dictionary<string, Action<SensorDataDto, JsonElement>> SensorFieldMap =
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

            // ── Light (lux – preferred) ───────────────────────────────────
            ["illuminance_lux"]          = (d, e) => d.LightIntensity = GetDouble(e),
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
    /// The Zigbee2MQTT friendly name extracted from the MQTT topic. Used as
    /// the device identifier only when the payload contains no
    /// <c>ieee_address</c> (i.e. <c>include_device_information</c> is off).
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
            bool luxAlreadySet = false;
            string? rawIlluminanceJson = null; // defer raw illuminance

            foreach (var property in doc.RootElement.EnumerateObject())
            {
                // ── Stable hardware identifier (requires include_device_information: true)
                if (property.Name.Equals("ieee_address", StringComparison.OrdinalIgnoreCase)
                    && property.Value.ValueKind == JsonValueKind.String)
                {
                    var addr = property.Value.GetString();
                    if (!string.IsNullOrWhiteSpace(addr))
                    {
                        // Uppercase so it matches the value stored during auto-provisioning.
                        dto.MacAddress = addr.Trim().ToUpperInvariant();
                    }
                    continue;
                }

                // ── illuminance (raw) – only applied after all properties are
                //    scanned so that illuminance_lux can take priority.
                if (property.Name.Equals("illuminance", StringComparison.OrdinalIgnoreCase))
                {
                    rawIlluminanceJson = property.Value.GetRawText();
                    continue;
                }

                if (SensorFieldMap.TryGetValue(property.Name, out var setter))
                {
                    setter(dto, property.Value);
                    matched = true;
                    if (property.Name.Equals("illuminance_lux", StringComparison.OrdinalIgnoreCase)
                        || property.Name.Equals("light_intensity", StringComparison.OrdinalIgnoreCase))
                    {
                        luxAlreadySet = true;
                    }
                }
            }

            // Apply raw illuminance only when no lux value was present
            if (!luxAlreadySet && rawIlluminanceJson != null)
            {
                using var rawEl = JsonDocument.Parse(rawIlluminanceJson);
                dto.LightIntensity = GetDouble(rawEl.RootElement);
                matched = true;
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
