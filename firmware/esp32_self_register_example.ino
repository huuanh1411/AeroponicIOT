// Select the correct WiFi header for the active board core.
#if defined(ESP32)
#include <WiFi.h>
#elif defined(ESP8266)
#include <ESP8266WiFi.h>
#else
#error "Unsupported board core. Select an ESP32 or ESP8266 board in Arduino IDE."
#endif
#include <HTTPClient.h>
#include <ArduinoJson.h>
#include <PubSubClient.h>

// Set to 1 when you wire real sensors and install required libraries.
#define USE_REAL_SENSORS 0

#if USE_REAL_SENSORS
#include <Wire.h>
#include <DHT.h>
#include <BH1750.h>
#endif

// =========================
// User configuration
// =========================
const char* WIFI_SSID = "YOUR_WIFI_SSID";
const char* WIFI_PASSWORD = "YOUR_WIFI_PASSWORD";

// Example: "http://192.168.1.10:5062"
const char* API_BASE_URL = "http://YOUR_SERVER_IP:5062";

// Must match Provisioning:SharedKey on backend
const char* DEVICE_PROVISION_KEY = "YOUR_PROVISIONING_SHARED_KEY";

// Optional friendly name for this node
const char* DEVICE_NAME = "ESP32-Node-01";

// Bump when you flash new firmware
const char* FIRMWARE_VERSION = "1.0.0";

// Retry interval while waiting for claim
const unsigned long PROVISION_RETRY_MS = 30000;

// MQTT settings for normal operation after claim
const char* MQTT_BROKER_HOST = "YOUR_SERVER_IP";
const uint16_t MQTT_BROKER_PORT = 1883;
const char* MQTT_USERNAME = "";   // Optional. Set if your broker requires auth.
const char* MQTT_PASSWORD = "";   // Optional. Set if your broker requires auth.
const unsigned long SENSOR_PUBLISH_MS = 5000;

// =========================
// Sensor configuration
// =========================
// Analog calibration placeholders. Tune these with your real sensors.
const float ADC_REF_VOLTAGE = 3.3f;
const int ADC_MAX = 4095;            // ESP32 default ADC range
const float PH_NEUTRAL_VOLTAGE = 2.5f;
const float PH_SLOPE = -5.7f;        // Approx slope for pH sensor modules
const float TDS_FACTOR = 500.0f;     // Placeholder conversion factor

// Sensor pins (change to match your board wiring)
#if defined(ESP32)
const int PH_PIN = 34;
const int TDS_PIN = 35;
const int DHT_PIN = 4;
#else
const int PH_PIN = A0;
const int TDS_PIN = A0;
const int DHT_PIN = D4;
#endif

#if USE_REAL_SENSORS
#define DHT_TYPE DHT22
DHT dht(DHT_PIN, DHT_TYPE);
BH1750 lightMeter;
#endif

bool isClaimed = false;
unsigned long lastProvisionAttemptMs = 0;
unsigned long lastSensorPublishMs = 0;

WiFiClient wifiClient;
PubSubClient mqttClient(wifiClient);
String sensorTopic;
String controlTopic;

struct SensorSample {
  float ph;
  int tds;
  float waterTemperature;
  float airHumidity;
  int lightIntensity;
};

String getMacAddress() {
  return WiFi.macAddress();
}

String getChipId() {
#if defined(ESP32)
  uint64_t chipid = ESP.getEfuseMac();
  char buffer[17];
  snprintf(buffer, sizeof(buffer), "%04X%08X", (uint16_t)(chipid >> 32), (uint32_t)chipid);
  return String(buffer);
#else
  // ESP8266 fallback
  return String(ESP.getChipId(), HEX);
#endif
}

float fakePh() {
  return 5.8f + (random(-20, 21) / 100.0f);
}

int fakeTds() {
  return 750 + random(-100, 101);
}

float fakeWaterTemp() {
  return 22.0f + (random(-20, 21) / 10.0f);
}

float fakeAirHumidity() {
  return 65.0f + (random(-100, 101) / 10.0f);
}

int fakeLightIntensity() {
  return 12000 + random(-2500, 2501);
}

float readPhFromAnalog(int raw) {
  float voltage = (raw * ADC_REF_VOLTAGE) / ADC_MAX;
  // pH = 7 + slope * (V - neutralVoltage)
  return 7.0f + (PH_SLOPE * (voltage - PH_NEUTRAL_VOLTAGE));
}

int readTdsFromAnalog(int raw) {
  float voltage = (raw * ADC_REF_VOLTAGE) / ADC_MAX;
  return (int)(voltage * TDS_FACTOR);
}

void initializeSensors() {
#if USE_REAL_SENSORS
  dht.begin();
  Wire.begin();
  if (lightMeter.begin(BH1750::CONTINUOUS_HIGH_RES_MODE)) {
    Serial.println("BH1750 initialized");
  } else {
    Serial.println("BH1750 init failed");
  }
  pinMode(PH_PIN, INPUT);
  pinMode(TDS_PIN, INPUT);
  Serial.println("Real sensors initialized");
#else
  Serial.println("Simulation mode: using fake sensor values");
#endif
}

SensorSample readSensors() {
  SensorSample sample;

#if USE_REAL_SENSORS
  int phRaw = analogRead(PH_PIN);
  int tdsRaw = analogRead(TDS_PIN);

  sample.ph = readPhFromAnalog(phRaw);
  sample.tds = readTdsFromAnalog(tdsRaw);

  // If using DHT22, humidity is real; temperature here is used as water temp stub.
  // Replace with DS18B20 or waterproof probe for true water temperature.
  float t = dht.readTemperature();
  float h = dht.readHumidity();

  sample.waterTemperature = isnan(t) ? fakeWaterTemp() : t;
  sample.airHumidity = isnan(h) ? fakeAirHumidity() : h;

  float lux = lightMeter.readLightLevel();
  sample.lightIntensity = isnan(lux) ? fakeLightIntensity() : (int)lux;

  // Safety fallback if analog conversion is noisy/out-of-range.
  if (sample.ph < 0.0f || sample.ph > 14.0f) {
    sample.ph = fakePh();
  }
  if (sample.tds < 0) {
    sample.tds = fakeTds();
  }
#else
  sample.ph = fakePh();
  sample.tds = fakeTds();
  sample.waterTemperature = fakeWaterTemp();
  sample.airHumidity = fakeAirHumidity();
  sample.lightIntensity = fakeLightIntensity();
#endif

  return sample;
}

void onMqttMessage(char* topic, byte* payload, unsigned int length) {
  String body;
  body.reserve(length);
  for (unsigned int i = 0; i < length; i++) {
    body += (char)payload[i];
  }

  Serial.println("MQTT control message received");
  Serial.print("Topic: ");
  Serial.println(topic);
  Serial.print("Payload: ");
  Serial.println(body);

  StaticJsonDocument<256> doc;
  if (deserializeJson(doc, body)) {
    Serial.println("Control payload is not valid JSON");
    return;
  }

  const char* action = doc["action"] | "";
  int actuatorType = doc["actuatorType"] | -1;

  // TODO: map actuatorType to actual GPIO relay outputs.
  Serial.print("Control action: ");
  Serial.print(action);
  Serial.print(" | actuatorType: ");
  Serial.println(actuatorType);
}

void ensureMqttConnected() {
  if (mqttClient.connected()) {
    return;
  }

  Serial.print("Connecting MQTT...");
  String clientId = String("esp-") + getMacAddress();
  clientId.replace(":", "");

  bool connected;
  if (strlen(MQTT_USERNAME) > 0) {
    connected = mqttClient.connect(clientId.c_str(), MQTT_USERNAME, MQTT_PASSWORD);
  } else {
    connected = mqttClient.connect(clientId.c_str());
  }

  if (!connected) {
    Serial.print(" failed, rc=");
    Serial.println(mqttClient.state());
    return;
  }

  Serial.println(" connected");
  if (mqttClient.subscribe(controlTopic.c_str())) {
    Serial.print("Subscribed control topic: ");
    Serial.println(controlTopic);
  } else {
    Serial.println("Failed to subscribe control topic");
  }
}

void publishSensorData() {
  if (!mqttClient.connected()) {
    return;
  }

  SensorSample sample = readSensors();

  StaticJsonDocument<256> doc;
  doc["macAddress"] = getMacAddress();
  doc["ph"] = sample.ph;
  doc["tds"] = sample.tds;
  doc["waterTemperature"] = sample.waterTemperature;
  doc["airHumidity"] = sample.airHumidity;
  doc["lightIntensity"] = sample.lightIntensity;

  String payload;
  serializeJson(doc, payload);

  bool ok = mqttClient.publish(sensorTopic.c_str(), payload.c_str(), false);
  Serial.print("Publish sensor ");
  Serial.print(ok ? "OK" : "FAILED");
  Serial.print(" => ");
  Serial.println(payload);
}

void connectWiFi() {
  Serial.print("Connecting to WiFi");
  WiFi.mode(WIFI_STA);
  WiFi.begin(WIFI_SSID, WIFI_PASSWORD);

  int attempts = 0;
  while (WiFi.status() != WL_CONNECTED && attempts < 60) {
    delay(500);
    Serial.print(".");
    attempts++;
  }

  if (WiFi.status() == WL_CONNECTED) {
    Serial.println();
    Serial.println("WiFi connected");
    Serial.print("IP: ");
    Serial.println(WiFi.localIP());
  } else {
    Serial.println();
    Serial.println("WiFi connection failed");
  }
}

bool selfRegisterDevice() {
  if (WiFi.status() != WL_CONNECTED) {
    Serial.println("Cannot self-register: WiFi not connected");
    return false;
  }

  HTTPClient http;
  String url = String(API_BASE_URL) + "/api/device/self-register";

  Serial.print("Self-registering at: ");
  Serial.println(url);

  http.begin(url);
  http.addHeader("Content-Type", "application/json");
  http.addHeader("X-Device-Key", DEVICE_PROVISION_KEY);

  StaticJsonDocument<256> payload;
  payload["macAddress"] = getMacAddress();
  payload["deviceName"] = DEVICE_NAME;
  payload["chipId"] = getChipId();
  payload["firmwareVersion"] = FIRMWARE_VERSION;

  String requestBody;
  serializeJson(payload, requestBody);

  int httpCode = http.POST(requestBody);
  String responseBody = http.getString();
  http.end();

  Serial.print("Provision HTTP code: ");
  Serial.println(httpCode);
  Serial.print("Provision response: ");
  Serial.println(responseBody);

  if (httpCode < 200 || httpCode >= 300) {
    return false;
  }

  StaticJsonDocument<512> response;
  DeserializationError err = deserializeJson(response, responseBody);
  if (err) {
    Serial.print("JSON parse error: ");
    Serial.println(err.c_str());
    return false;
  }

  bool success = response["success"] | false;
  bool alreadyClaimed = response["alreadyClaimed"] | false;

  if (!success) {
    Serial.println("Provision failed: success=false");
    return false;
  }

  if (alreadyClaimed) {
    isClaimed = true;
    Serial.println("Device is already claimed. Starting normal operation.");
    return true;
  }

  const char* claimCode = response["claimCode"] | "";
  const char* expiresAt = response["claimCodeExpiresAt"] | "";

  Serial.println("====================================================");
  Serial.println("Device waiting for claim.");
  Serial.print("Claim code: ");
  Serial.println(claimCode);
  Serial.print("Expires at: ");
  Serial.println(expiresAt);
  Serial.println("Enter this code in dashboard Devices page to claim.");
  Serial.println("====================================================");

  return true;
}

void setup() {
  Serial.begin(115200);
  delay(1000);

  Serial.println();
  Serial.println("ESP32 self-register bootstrap starting...");
  Serial.print("MAC: ");
  Serial.println(getMacAddress());
  Serial.print("Chip ID: ");
  Serial.println(getChipId());

  connectWiFi();

  randomSeed((uint32_t)ESP.getCycleCount());

  sensorTopic = String("devices/") + getMacAddress() + "/sensor";
  controlTopic = String("devices/") + getMacAddress() + "/control";

  mqttClient.setServer(MQTT_BROKER_HOST, MQTT_BROKER_PORT);
  mqttClient.setCallback(onMqttMessage);
  initializeSensors();

  // First immediate provisioning attempt on boot
  selfRegisterDevice();
  lastProvisionAttemptMs = millis();
}

void loop() {
  // Keep WiFi connected
  if (WiFi.status() != WL_CONNECTED) {
    connectWiFi();
  }

  // If not claimed yet, retry provisioning periodically
  if (!isClaimed && millis() - lastProvisionAttemptMs >= PROVISION_RETRY_MS) {
    selfRegisterDevice();
    lastProvisionAttemptMs = millis();
  }

  if (isClaimed) {
    ensureMqttConnected();
    mqttClient.loop();

    if (millis() - lastSensorPublishMs >= SENSOR_PUBLISH_MS) {
      publishSensorData();
      lastSensorPublishMs = millis();
    }
  }

  delay(200);
}
