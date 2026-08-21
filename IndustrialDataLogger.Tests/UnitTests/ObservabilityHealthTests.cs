using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using IndustrialDataLogger.Enums;
using IndustrialDataLogger.HealthChecks;
using IndustrialDataLogger.Models;
using IndustrialDataLogger.Services;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace IndustrialDataLogger.Tests.UnitTests
{
    public class ObservabilityHealthTests
    {
        [Fact]
        public async Task PlcHealthCheck_WhenConnected_ShouldReturnHealthy()
        {
            // Arrange
            var mockPlc = new MockPlcService();
            mockPlc.SetScenario(SimulationScenario.Normal);
            var logger = NullLogger<PlcConnectionManager>.Instance;
            var connectionManager = new PlcConnectionManager(null!, mockPlc, logger);
            await connectionManager.ConnectAsync();

            var healthCheck = new PlcHealthCheck(connectionManager);

            // Act
            var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

            // Assert
            Assert.Equal(HealthStatus.Healthy, result.Status);
            Assert.True((bool)result.Data["IsConnected"]);
        }

        [Fact]
        public async Task PlcHealthCheck_WhenDisconnected_ShouldReturnUnhealthy()
        {
            // Arrange
            var mockPlc = new MockPlcService();
            mockPlc.SetScenario(SimulationScenario.PlcFailure);
            var logger = NullLogger<PlcConnectionManager>.Instance;
            var connectionManager = new PlcConnectionManager(null!, mockPlc, logger);
            await connectionManager.DisconnectAsync();

            var healthCheck = new PlcHealthCheck(connectionManager);

            // Act
            var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

            // Assert
            Assert.Equal(HealthStatus.Unhealthy, result.Status);
            Assert.False((bool)result.Data["IsConnected"]);
        }
    }
}
