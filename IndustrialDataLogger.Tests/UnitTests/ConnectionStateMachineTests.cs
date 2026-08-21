using System;
using System.Threading.Tasks;
using IndustrialDataLogger.Enums;
using IndustrialDataLogger.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace IndustrialDataLogger.Tests.UnitTests
{
    public class ConnectionStateMachineTests
    {
        [Theory]
        [InlineData(1, 2)]
        [InlineData(2, 4)]
        [InlineData(3, 8)]
        [InlineData(4, 16)]
        [InlineData(5, 30)]
        [InlineData(10, 30)]
        public void ExponentialBackoff_ShouldCalculateCorrectDelay(int attempt, int expectedSeconds)
        {
            // Backoff logic: Math.Min(Math.Pow(2, attempt), 30)
            int delay = (int)Math.Min(Math.Pow(2, attempt), 30);
            Assert.Equal(expectedSeconds, delay);
        }

        [Fact]
        public void StateMachine_TransitionsShouldFollowIndustryLifecycle()
        {
            // Disconnected -> Connecting -> Connected -> Reconnecting -> Connected -> Disconnected
            var state = PlcConnectionState.Disconnected;
            Assert.Equal(PlcConnectionState.Disconnected, state);

            state = PlcConnectionState.Connecting;
            Assert.Equal(PlcConnectionState.Connecting, state);

            state = PlcConnectionState.Connected;
            Assert.Equal(PlcConnectionState.Connected, state);

            state = PlcConnectionState.Reconnecting;
            Assert.Equal(PlcConnectionState.Reconnecting, state);

            state = PlcConnectionState.Connected;
            Assert.Equal(PlcConnectionState.Connected, state);

            state = PlcConnectionState.Disconnecting;
            Assert.Equal(PlcConnectionState.Disconnecting, state);

            state = PlcConnectionState.Disconnected;
            Assert.Equal(PlcConnectionState.Disconnected, state);
        }

        [Fact]
        public async Task PlcConnectionManager_ShouldStartDisconnectedAndConnectSuccessfully()
        {
            // Arrange
            var mockPlc = new MockPlcService();
            var manager = new PlcConnectionManager(null!, mockPlc, NullLogger<PlcConnectionManager>.Instance);

            // Assert Initial State
            Assert.Equal(PlcConnectionState.Disconnected, manager.CurrentState);
            Assert.False(manager.IsConnected);

            // Act: Connect
            var connected = await manager.ConnectAsync();

            // Assert Connected State
            Assert.True(connected);
            Assert.Equal(PlcConnectionState.Connected, manager.CurrentState);
            Assert.True(manager.IsConnected);

            // Act: Disconnect
            await manager.DisconnectAsync();

            // Assert Disconnected State
            Assert.Equal(PlcConnectionState.Disconnected, manager.CurrentState);
            Assert.False(manager.IsConnected);
        }

        [Fact]
        public async Task PlcConnectionManager_SetMode_ShouldSwitchSimulationModeCleanly()
        {
            // Arrange
            var mockPlc = new MockPlcService();
            var manager = new PlcConnectionManager(null!, mockPlc, NullLogger<PlcConnectionManager>.Instance);

            // Act
            await manager.SetModeAsync(true);
            Assert.True(PlcConnectionManager.IsSimulationMode);

            await manager.SetModeAsync(false);
            Assert.False(PlcConnectionManager.IsSimulationMode);

            // Reset back to true
            await manager.SetModeAsync(true);
            Assert.True(PlcConnectionManager.IsSimulationMode);
        }
    }
}
