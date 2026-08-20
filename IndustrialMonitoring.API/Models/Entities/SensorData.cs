namespace IndustrialMonitoring.API.Models.Entities
{
    public class SensorData
    {
        public int Id { get; set; }
        public string SensorName { get; set; } = string.Empty;
        public decimal Value { get; set; }
        public string Unit { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public string Status { get; set; } = string.Empty; // <-- Buradaki ':' karakteri '=' ile değiştirildi
    }
}