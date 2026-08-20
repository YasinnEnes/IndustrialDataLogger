using System.Collections.Generic;
using IndustrialDataLogger.Services;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace IndustrialDataLogger.Tests.UnitTests
{
    public class SecurityAuthTests
    {
        [Fact]
        public void JwtTokenService_ShouldGenerateValidSignedToken()
        {
            // Arrange
            var inMemorySettings = new Dictionary<string, string?>
            {
                { "JwtSettings:Secret", "Unit_Test_Super_Secret_Jwt_Key_For_Industrial_Logger_2026_Key" },
                { "JwtSettings:Issuer", "IndustrialDataLoggerAPI" },
                { "JwtSettings:Audience", "IndustrialDataLoggerDashboard" },
                { "JwtSettings:ExpiryMinutes", "60" }
            };

            IConfiguration configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(inMemorySettings)
                .Build();

            var tokenService = new JwtTokenService(configuration);

            // Act
            var token = tokenService.GenerateToken("admin", "Admin");

            // Assert
            Assert.NotNull(token);
            Assert.NotEmpty(token);
            Assert.Contains(".", token); // JWT contains 3 segments separated by dots
            Assert.Equal(2, token.Split('.').Length - 1);
        }
    }
}
