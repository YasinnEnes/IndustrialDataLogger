namespace IndustrialDataLogger.Enums
{
    public enum HealthGrade
    {
        Critical = 0,   // < 40%
        Degraded = 1,   // 40% - 64%
        Warning = 2,    // 65% - 84%
        Healthy = 3     // >= 85%
    }
}
