public record SensorBinding (
    string BluetoothMacAddress,
    string Webhook,
    string HttpMethod = "PUT",
    string JsonFieldName = "sensorValue"
);