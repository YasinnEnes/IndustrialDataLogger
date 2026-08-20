using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using IndustrialDataLogger.Enums;
using IndustrialDataLogger.Services;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace IndustrialDataLogger.HealthChecks
{
    public class PlcHealthCheck : IHealthCheck
    {
        private readonly IPlcConnectionManager _connectionManager;

        public PlcHealthCheck(IPlcConnectionManager connectionManager)
        {
            _connectionManager = connectionManager;
        }

        public Task<HealthCheckResult> CheckHealthAsync(
            HealthCheckContext context,
            CancellationToken cancellationToken = default)
        {
            var state = _connectionManager.CurrentState;
            var isConnected = _connectionManager.IsConnected;
            var scenario = _connectionManager.CurrentScenario;

            var data = new Dictionary<string, object>
            {
                { "ConnectionState", state.ToString() },
                { "IsConnected", isConnected },
                { "Scenario", scenario.ToString() },
                { "IsSimulationMode", PlcConnectionManager.IsSimulationMode }
            };

            if (isConnected)
            {
                return Task.FromResult(HealthCheckResult.Healthy(
                    $"PLC bağlantısı aktif ({state}).", data));
            }

            if (state == PlcConnectionState.Reconnecting || state == PlcConnectionState.Connecting)
            {
                return Task.FromResult(HealthCheckResult.Degraded(
                    $"PLC bağlantısı kuruluyor/yeniden bağlanıyor ({state}).", null, data));
            }

            return Task.FromResult(HealthCheckResult.Unhealthy(
                $"PLC bağlantısı kesik ({state}).", null, data));
        }
    }
}
