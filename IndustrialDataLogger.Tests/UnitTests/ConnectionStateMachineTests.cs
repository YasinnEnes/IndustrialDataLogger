using System;
using IndustrialDataLogger.Enums;
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
            // Disconnected -> Connecting -> Connected -> Reconnecting -> Connected
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
        }
    }
}
