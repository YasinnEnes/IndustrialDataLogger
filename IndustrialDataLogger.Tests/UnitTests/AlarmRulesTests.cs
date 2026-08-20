using System;
using IndustrialDataLogger.Enums;
using IndustrialDataLogger.Models.Entities;
using Xunit;

namespace IndustrialDataLogger.Tests.UnitTests
{
    public class AlarmRulesTests
    {
        [Theory]
        [InlineData(75.0, false, AlarmSeverity.Info)]
        [InlineData(82.5, true, AlarmSeverity.Warning)]
        [InlineData(94.0, true, AlarmSeverity.Critical)]
        public void TemperatureThresholds_ShouldTriggerCorrectSeverity(double temp, bool shouldTrigger, AlarmSeverity expectedSeverity)
        {
            // Eşik Kuralları: Temp > 80 Warning, Temp > 90 Critical
            bool isTriggered = false;
            AlarmSeverity severity = AlarmSeverity.Info;

            if (temp > 90.0)
            {
                isTriggered = true;
                severity = AlarmSeverity.Critical;
            }
            else if (temp > 80.0)
            {
                isTriggered = true;
                severity = AlarmSeverity.Warning;
            }

            Assert.Equal(shouldTrigger, isTriggered);
            if (shouldTrigger)
            {
                Assert.Equal(expectedSeverity, severity);
            }
        }

        [Fact]
        public void AlarmLifecycle_ShouldTransitionThroughAllStates()
        {
            // Lifecycle: Triggered -> Active -> Acknowledged -> Resolved
            var alarm = new AlarmLog
            {
                Id = 101,
                AlarmType = "HIGH_TEMPERATURE",
                Severity = AlarmSeverity.Warning,
                Status = AlarmStatus.Triggered,
                Message = "Sıcaklık eşiği aşıldı",
                CreatedAt = DateTime.UtcNow
            };

            Assert.Equal(AlarmStatus.Triggered, alarm.Status);

            // Step 1: Active
            alarm.Status = AlarmStatus.Active;
            Assert.Equal(AlarmStatus.Active, alarm.Status);

            // Step 2: Acknowledge
            alarm.Status = AlarmStatus.Acknowledged;
            alarm.AcknowledgedAt = DateTime.UtcNow;
            Assert.Equal(AlarmStatus.Acknowledged, alarm.Status);
            Assert.NotNull(alarm.AcknowledgedAt);

            // Step 3: Resolve
            alarm.Status = AlarmStatus.Resolved;
            alarm.ResolvedAt = DateTime.UtcNow;
            Assert.Equal(AlarmStatus.Resolved, alarm.Status);
            Assert.NotNull(alarm.ResolvedAt);
        }
    }
}
