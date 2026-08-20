using System;

namespace IndustrialDataLogger.Models.DTOs
{
    public class OeeMetricsDto
    {
        public double Availability { get; set; } = 95.0; // Kullanılabilirlik % (Çalışma Süresi / Planlanan Süre)
        public double Performance { get; set; } = 92.5;  // Performans % (Gerçekleşen Hız / İdeal Hız)
        public double Quality { get; set; } = 99.1;      // Kalite % (Sağlam Parça / Toplam Parça)
        public double OverallOee { get; set; } = 87.1;   // Toplam OEE % (Availability * Performance * Quality)
        
        public long TotalCycleCount { get; set; } = 1450; // Toplam Üretilen Parça
        public long GoodPartCount { get; set; } = 1437;   // Sağlam Ürün
        public long DefectCount { get; set; } = 13;       // Hatalı/Fire Ürün
        
        public double RuntimeMinutes { get; set; } = 480; // Çalışma Süresi (Dakika)
        public double DowntimeMinutes { get; set; } = 25; // Duruş Süresi (Dakika)
    }
}
