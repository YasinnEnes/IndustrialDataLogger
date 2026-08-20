using System;

namespace IndustrialDataLogger.Models
{
    public class SensorData
    {
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public double Temperature { get; set; }
        public double Pressure { get; set; }
        public bool MachineStatus { get; set; }
        public short ErrorCode { get; set; }
    }

    public class PlcWriteRequest
    {
        public string VariableName { get; set; } = string.Empty;
        public string DataType { get; set; } = "REAL"; // "REAL", "BOOL", "INT", "DINT", "STRING"
        public object Value { get; set; } = default!;
    }

    // Yetkilendirilmiş Dinamik Veri Tipli Komut Modeli
    public class CommandModel
    {
        public string TargetParameter { get; set; } = "TemperatureSetpoint";
        public string VariableName { get; set; } = "DB1.DBD0";
        public string DataType { get; set; } = "REAL"; // "REAL", "BOOL", "INT", "DINT", "STRING"
        public string Value { get; set; } = string.Empty;
        public double? Setpoint { get; set; } // Geriye dönük uyumluluk için
    }
}