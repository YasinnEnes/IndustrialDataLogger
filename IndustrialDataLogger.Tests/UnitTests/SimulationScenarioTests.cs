using System.Threading.Tasks;
using IndustrialDataLogger.Enums;
using IndustrialDataLogger.Services;
using Xunit;

namespace IndustrialDataLogger.Tests.UnitTests
{
    public class SimulationScenarioTests
    {
        [Fact]
        public void NormalScenario_ShouldProduceNominalTelemetryValues()
        {
            // Arrange
            var mockPlc = new MockPlcService();
            mockPlc.SetScenario(SimulationScenario.Normal);

            // Act
            var data = mockPlc.ReadSensorData();

            // Assert
            Assert.NotNull(data);
            Assert.True(data!.MachineStatus);
            Assert.InRange(data.Temperature, 25.0, 65.0);
            Assert.InRange(data.Pressure, 3.0, 8.0);
            Assert.Equal((short)0, data.ErrorCode);
        }

        [Fact]
        public void OverheatingScenario_ShouldEscalateTemperatureAboveThresholds()
        {
            // Arrange
            var mockPlc = new MockPlcService();
            mockPlc.SetScenario(SimulationScenario.Overheating);

            // Act - Sıcaklık tırmanışını doğrulamak için birkaç adım oku
            mockPlc.ReadSensorData();
            var finalData = mockPlc.ReadSensorData();

            // Assert
            Assert.NotNull(finalData);
            Assert.True(finalData!.Temperature >= 80.0, $"Sıcaklık 80°C üzerinde olmalı, ancak {finalData.Temperature}°C");
        }

        [Fact]
        public void HighPressureScenario_ShouldEscalatePressureAboveThresholds()
        {
            // Arrange
            var mockPlc = new MockPlcService();
            mockPlc.SetScenario(SimulationScenario.HighPressure);

            // Act
            mockPlc.ReadSensorData();
            var finalData = mockPlc.ReadSensorData();

            // Assert
            Assert.NotNull(finalData);
            Assert.True(finalData!.Pressure >= 8.0, $"Basınç 8.0 bar üzerinde olmalı, ancak {finalData.Pressure} bar");
        }

        [Fact]
        public void MachineStopScenario_ShouldMarkMachineStatusStopped()
        {
            // Arrange
            var mockPlc = new MockPlcService();
            mockPlc.SetScenario(SimulationScenario.MachineStop);

            // Act
            var data = mockPlc.ReadSensorData();

            // Assert
            Assert.NotNull(data);
            Assert.False(data!.MachineStatus, "Makine durumu kapalı (false) olmalıdır.");
        }

        [Fact]
        public void PlcFailureScenario_ShouldReturnNullSensorData()
        {
            // Arrange
            var mockPlc = new MockPlcService();
            mockPlc.SetScenario(SimulationScenario.PlcFailure);

            // Act
            var data = mockPlc.ReadSensorData();

            // Assert
            Assert.Null(data);
            Assert.False(mockPlc.IsConnected);
        }
    }
}
