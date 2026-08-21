using System;
using System.Collections.Generic;
using System.Linq;
using IndustrialDataLogger.Enums;
using IndustrialDataLogger.Models.Entities;
using IndustrialDataLogger.Services;
using Xunit;

namespace IndustrialDataLogger.Tests.UnitTests
{
    public class AlarmRulesTests
    {
        [Theory]
        [InlineData(75.0, ComparisonOperator.GreaterThan, 80.0, false)]
        [InlineData(80.0, ComparisonOperator.GreaterThan, 80.0, false)]
        [InlineData(80.1, ComparisonOperator.GreaterThan, 80.0, true)]
        [InlineData(80.0, ComparisonOperator.GreaterThanOrEqual, 80.0, true)]
        [InlineData(79.9, ComparisonOperator.LessThan, 80.0, true)]
        [InlineData(80.0, ComparisonOperator.LessThan, 80.0, false)]
        [InlineData(80.0, ComparisonOperator.LessThanOrEqual, 80.0, true)]
        [InlineData(80.0, ComparisonOperator.Equal, 80.0, true)]
        [InlineData(80.5, ComparisonOperator.Equal, 80.0, false)]
        [InlineData(85.0, ComparisonOperator.NotEqual, 80.0, true)]
        [InlineData(80.0, ComparisonOperator.NotEqual, 80.0, false)]
        public void EvaluateCondition_ShouldEvaluateAllOperatorsAccurately(
            double value,
            ComparisonOperator op,
            double threshold,
            bool expectedResult)
        {
            // Act
            bool result = AlarmService.EvaluateCondition(value, op, threshold);

            // Assert
            Assert.Equal(expectedResult, result);
        }

        [Fact]
        public void AlarmRule_ShouldSupportMachineSpecificAndGlobalRules()
        {
            // Arrange
            var globalTempRule = new AlarmRule
            {
                Id = 1,
                MachineId = null, // Global
                RuleName = "Genel Yüksek Sıcaklık",
                Metric = "Temperature",
                Operator = ComparisonOperator.GreaterThan,
                Threshold = 80.0,
                Severity = AlarmSeverity.Warning,
                AlarmType = "HIGH_TEMPERATURE",
                Enabled = true
            };

            var machineSpecificRule = new AlarmRule
            {
                Id = 2,
                MachineId = 3, // Sadece Makine #3
                RuleName = "CNC Özel Sıcaklık",
                Metric = "Temperature",
                Operator = ComparisonOperator.GreaterThan,
                Threshold = 65.0,
                Severity = AlarmSeverity.Critical,
                AlarmType = "CNC_HIGH_TEMP",
                Enabled = true
            };

            var rules = new List<AlarmRule> { globalTempRule, machineSpecificRule };

            // Act: Makine 1 için uygulanabilir kurallar
            var machine1Rules = rules.Where(r => r.Enabled && (r.MachineId == null || r.MachineId == 1)).ToList();
            // Act: Makine 3 için uygulanabilir kurallar
            var machine3Rules = rules.Where(r => r.Enabled && (r.MachineId == null || r.MachineId == 3)).ToList();

            // Assert
            Assert.Single(machine1Rules);
            Assert.Equal("Genel Yüksek Sıcaklık", machine1Rules[0].RuleName);

            Assert.Equal(2, machine3Rules.Count);
            Assert.Contains(machine3Rules, r => r.RuleName == "CNC Özel Sıcaklık");
            Assert.Contains(machine3Rules, r => r.RuleName == "Genel Yüksek Sıcaklık");
        }

        [Theory]
        [InlineData(75.0, false, AlarmSeverity.Info, null)]
        [InlineData(82.5, true, AlarmSeverity.Warning, "HIGH_TEMPERATURE")]
        [InlineData(94.0, true, AlarmSeverity.Critical, "CRITICAL_TEMPERATURE")]
        public void MultiSeverityRules_ShouldTriggerHighestPriorityAlarm(
            double temp,
            bool shouldTrigger,
            AlarmSeverity expectedSeverity,
            string? expectedAlarmType)
        {
            // Arrange
            var warningRule = new AlarmRule
            {
                Id = 1,
                RuleName = "Yüksek Sıcaklık Uyarısı",
                Metric = "Temperature",
                Operator = ComparisonOperator.GreaterThan,
                Threshold = 80.0,
                Severity = AlarmSeverity.Warning,
                AlarmType = "HIGH_TEMPERATURE"
            };

            var criticalRule = new AlarmRule
            {
                Id = 2,
                RuleName = "Kritik Sıcaklık Tehlikesi",
                Metric = "Temperature",
                Operator = ComparisonOperator.GreaterThan,
                Threshold = 90.0,
                Severity = AlarmSeverity.Critical,
                AlarmType = "CRITICAL_TEMPERATURE"
            };

            var rules = new List<AlarmRule> { warningRule, criticalRule }
                .OrderByDescending(r => r.Severity)
                .ToList();

            // Act
            AlarmRule? triggeredRule = null;
            foreach (var rule in rules)
            {
                if (AlarmService.EvaluateCondition(temp, rule.Operator, rule.Threshold))
                {
                    triggeredRule = rule;
                    break; // En yüksek severity önce tetiklenir
                }
            }

            // Assert
            if (!shouldTrigger)
            {
                Assert.Null(triggeredRule);
            }
            else
            {
                Assert.NotNull(triggeredRule);
                Assert.Equal(expectedSeverity, triggeredRule!.Severity);
                Assert.Equal(expectedAlarmType, triggeredRule.AlarmType);
            }
        }

        [Fact]
        public void AlarmLifecycle_ShouldTransitionThroughAllStates_FromNormalToResolved()
        {
            // 1. Durum: NORMAL (Tetiklenme yok)
            bool isNormal = true;
            Assert.True(isNormal);

            // 2. Durum: TRIGGERED (Kural ihlali tespit edildi)
            var alarm = new AlarmLog
            {
                Id = 101,
                MachineId = 1,
                AlarmType = "HIGH_TEMPERATURE",
                Severity = AlarmSeverity.Warning,
                Status = AlarmStatus.Triggered,
                Message = "Sıcaklık 82.5°C eşiği aştı!",
                TriggeredValue = 82.5,
                ThresholdValue = 80.0,
                CreatedAt = DateTime.UtcNow
            };
            Assert.Equal(AlarmStatus.Triggered, alarm.Status);

            // 3. Durum: ACTIVE (Sistemde aktif alarm olarak kayıtlı ve izleniyor)
            alarm.Status = AlarmStatus.Active;
            Assert.Equal(AlarmStatus.Active, alarm.Status);
            Assert.Null(alarm.AcknowledgedAt);
            Assert.Null(alarm.ResolvedAt);

            // 4. Durum: ACKNOWLEDGED (Operatör alarmı gördü ve onayladı)
            alarm.Status = AlarmStatus.Acknowledged;
            alarm.AcknowledgedAt = DateTime.UtcNow;
            Assert.Equal(AlarmStatus.Acknowledged, alarm.Status);
            Assert.NotNull(alarm.AcknowledgedAt);
            Assert.Null(alarm.ResolvedAt);

            // 5. Durum: RESOLVED (Telemetri değeri normale döndü ve alarm çözüldü)
            alarm.Status = AlarmStatus.Resolved;
            alarm.ResolvedAt = DateTime.UtcNow;
            Assert.Equal(AlarmStatus.Resolved, alarm.Status);
            Assert.NotNull(alarm.ResolvedAt);
            Assert.True(alarm.ResolvedAt >= alarm.CreatedAt);
        }
    }
}
