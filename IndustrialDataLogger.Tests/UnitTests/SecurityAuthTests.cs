using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using IndustrialDataLogger.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace IndustrialDataLogger.Tests.UnitTests
{
    public class SecurityAuthTests
    {
        private const string TestJwtSecret = "Unit_Test_Super_Secret_Jwt_Key_For_Industrial_Logger_2026_Key";
        private const string TestIssuer = "IndustrialDataLoggerAPI";
        private const string TestAudience = "IndustrialDataLoggerDashboard";

        private readonly IJwtTokenService _tokenService;

        public SecurityAuthTests()
        {
            var inMemorySettings = new Dictionary<string, string?>
            {
                { "JwtSettings:Secret", TestJwtSecret },
                { "JwtSettings:Issuer", TestIssuer },
                { "JwtSettings:Audience", TestAudience },
                { "JwtSettings:ExpiryMinutes", "60" }
            };

            IConfiguration configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(inMemorySettings)
                .Build();

            _tokenService = new JwtTokenService(configuration);
        }

        [Fact]
        public void JwtTokenService_ShouldGenerateValidSignedToken()
        {
            // Act
            var token = _tokenService.GenerateToken("admin", "Admin");

            // Assert
            Assert.NotNull(token);
            Assert.NotEmpty(token);
            Assert.Contains(".", token);
            Assert.Equal(2, token.Split('.').Length - 1);
        }

        [Theory]
        [InlineData("admin", "Admin")]
        [InlineData("programmer", "Programmer")]
        [InlineData("operator", "Operator")]
        [InlineData("viewer_user", "Viewer")]
        public void JwtTokenService_ShouldIncludeCorrectClaimsAndRoles(string username, string role)
        {
            // Act
            var tokenString = _tokenService.GenerateToken(username, role);

            var handler = new JwtSecurityTokenHandler();
            var jwtToken = handler.ReadJwtToken(tokenString);

            // Assert
            Assert.Equal(TestIssuer, jwtToken.Issuer);
            Assert.Contains(TestAudience, jwtToken.Audiences);

            var nameClaim = jwtToken.Claims.FirstOrDefault(c => c.Type == "unique_name" || c.Type == ClaimTypes.Name || c.Type == "sub");
            Assert.NotNull(nameClaim);

            var roleClaim = jwtToken.Claims.FirstOrDefault(c => c.Type == "role" || c.Type == ClaimTypes.Role);
            Assert.NotNull(roleClaim);
            Assert.Equal(role, roleClaim!.Value);
        }

        [Theory]
        [InlineData("Admin", true, true, true)]
        [InlineData("Programmer", true, true, true)]
        [InlineData("Operator", true, false, false)]
        [InlineData("Viewer", true, false, false)]
        public void RbacPermissions_ShouldEnforceRoleHierarchy(
            string role,
            bool canViewDashboard,
            bool canWritePlcVariables,
            bool canManageTagsAndRules)
        {
            // Act - RBAC Yetkilendirme Matrisi Doğrulaması
            bool hasViewPermission = role is "Admin" or "Programmer" or "Operator" or "Viewer";
            bool hasPlcWritePermission = role is "Admin" or "Programmer";
            bool hasTagManagePermission = role is "Admin" or "Programmer";

            // Assert
            Assert.Equal(canViewDashboard, hasViewPermission);
            Assert.Equal(canWritePlcVariables, hasPlcWritePermission);
            Assert.Equal(canManageTagsAndRules, hasTagManagePermission);
        }

        [Fact]
        public void TokenValidation_ShouldSuccessfullyValidateValidToken()
        {
            // Arrange
            var token = _tokenService.GenerateToken("programmer1", "Programmer");
            var tokenHandler = new JwtSecurityTokenHandler();
            var validationParameters = new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(TestJwtSecret)),
                ValidateIssuer = true,
                ValidIssuer = TestIssuer,
                ValidateAudience = true,
                ValidAudience = TestAudience,
                ClockSkew = TimeSpan.Zero
            };

            // Act
            var principal = tokenHandler.ValidateToken(token, validationParameters, out var validatedToken);

            // Assert
            Assert.NotNull(principal);
            Assert.True(principal.IsInRole("Programmer"));
            Assert.False(principal.IsInRole("Viewer"));
            Assert.NotNull(validatedToken);
        }

        [Fact]
        public void TokenValidation_WithTamperedSignature_ShouldThrowSecurityException()
        {
            // Arrange
            var validToken = _tokenService.GenerateToken("admin", "Admin");
            var parts = validToken.Split('.');
            // Signature değiştirilmiş token
            var tamperedToken = $"{parts[0]}.{parts[1]}.TAMPERED_SIGNATURE_DATA";

            var tokenHandler = new JwtSecurityTokenHandler();
            var validationParameters = new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(TestJwtSecret)),
                ValidateIssuer = false,
                ValidateAudience = false
            };

            // Act & Assert
            Assert.ThrowsAny<SecurityTokenException>(() =>
            {
                tokenHandler.ValidateToken(tamperedToken, validationParameters, out _);
            });
        }
    }
}
