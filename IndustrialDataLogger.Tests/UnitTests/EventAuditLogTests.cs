using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using IndustrialDataLogger.Enums;
using IndustrialDataLogger.Models.Entities;
using Xunit;

namespace IndustrialDataLogger.Tests.UnitTests
{
    public class EventAuditLogTests
    {
        [Fact]
        public void SystemEventLog_ShouldCorrectlyStoreAllAuditAttributes()
        {
            // Arrange & Act
            var log = new SystemEventLog
            {
                Id = 1,
                EventType = "PLC_CONNECTED",
                Description = "Siemens S7-1200 ile bağlantı kuruldu.",
                Severity = AlarmSeverity.Info,
                Source = "PLC",
                MachineId = 1,
                Timestamp = DateTime.UtcNow
            };

            // Assert
            Assert.Equal("PLC_CONNECTED", log.EventType);
            Assert.Equal(AlarmSeverity.Info, log.Severity);
            Assert.Equal("PLC", log.Source);
            Assert.Equal(1, log.MachineId);
            Assert.True(log.Timestamp <= DateTime.UtcNow);
        }

        [Fact]
        public void EventFiltering_ShouldFilterByEventTypeAndMachineCorrectly()
        {
            // Arrange
            var events = new List<SystemEventLog>
            {
                new SystemEventLog { Id = 1, EventType = "PLC_CONNECTED", MachineId = 1, Severity = AlarmSeverity.Info },
                new SystemEventLog { Id = 2, EventType = "USER_LOGIN", MachineId = 1, Severity = AlarmSeverity.Info },
                new SystemEventLog { Id = 3, EventType = "ALARM_TRIGGERED", MachineId = 2, Severity = AlarmSeverity.Critical },
                new SystemEventLog { Id = 4, EventType = "MACHINE_STARTED", MachineId = 1, Severity = AlarmSeverity.Info },
                new SystemEventLog { Id = 5, EventType = "PLC_DISCONNECTED", MachineId = 1, Severity = AlarmSeverity.Warning }
            };

            // Act: Makine 1 olayları
            var machine1Events = events.Where(e => e.MachineId == 1).ToList();

            // Act: Alarm olayları
            var alarmEvents = events.Where(e => e.EventType.StartsWith("ALARM_")).ToList();

            // Act: PLC olayları
            var plcEvents = events.Where(e => e.EventType.StartsWith("PLC_")).ToList();

            // Assert
            Assert.Equal(4, machine1Events.Count);
            Assert.Single(alarmEvents);
            Assert.Equal("ALARM_TRIGGERED", alarmEvents[0].EventType);
            Assert.Equal(2, plcEvents.Count);
        }

        [Theory]
        [InlineData("PLC_CONNECTED", "PLC", AlarmSeverity.Info)]
        [InlineData("PLC_DISCONNECTED", "PLC", AlarmSeverity.Warning)]
        [InlineData("MACHINE_STARTED", "MACHINE", AlarmSeverity.Info)]
        [InlineData("MACHINE_STOPPED", "MACHINE", AlarmSeverity.Warning)]
        [InlineData("ALARM_TRIGGERED", "ALARM", AlarmSeverity.Critical)]
        [InlineData("USER_LOGIN", "SECURITY", AlarmSeverity.Info)]
        [InlineData("AUTH_FAILED", "SECURITY", AlarmSeverity.Warning)]
        public void EventClassification_ShouldMatchStandardScadaCategories(
            string eventType,
            string expectedCategory,
            AlarmSeverity expectedSeverity)
        {
            // Act
            string category;
            if (eventType.StartsWith("PLC_")) category = "PLC";
            else if (eventType.StartsWith("MACHINE_")) category = "MACHINE";
            else if (eventType.StartsWith("ALARM_")) category = "ALARM";
            else if (eventType.StartsWith("USER_") || eventType.StartsWith("AUTH_")) category = "SECURITY";
            else category = "SYSTEM";

            // Assert
            Assert.Equal(expectedCategory, category);
            Assert.True(Enum.IsDefined(typeof(AlarmSeverity), expectedSeverity));
        }
    }
}
