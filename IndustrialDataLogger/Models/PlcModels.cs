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
        public object Value { get; set; } = default!;
    }
}