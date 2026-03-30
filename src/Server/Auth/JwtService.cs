using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace Dna.Auth;

public class JwtService
{
    private readonly string _secret;
    private readonly string _issuer = "project-dna";
    private readonly TimeSpan _expiry = TimeSpan.FromDays(7);

    public JwtService()
    {
        var envSecret = Environment.GetEnvironmentVariable("DNA_JWT_SECRET");
        _secret = !string.IsNullOrEmpty(envSecret) ? envSecret : GenerateAndPersistSecret();
    }

    public string GenerateToken(string userId, string username, string role)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_secret));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, userId),
            new Claim(JwtRegisteredClaimNames.UniqueName, username),
            new Claim(ClaimTypes.NameIdentifier, userId),
            new Claim(ClaimTypes.Name, username),
            new Claim(ClaimTypes.Role, role),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        var token = new JwtSecurityToken(
            issuer: _issuer,
            audience: _issuer,
            claims: claims,
            expires: DateTime.UtcNow.Add(_expiry),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public TokenValidationParameters GetValidationParameters() => new()
    {
        ValidateIssuer = true,
        ValidIssuer = _issuer,
        ValidateAudience = true,
        ValidAudience = _issuer,
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_secret)),
        NameClaimType = ClaimTypes.Name,
        RoleClaimType = ClaimTypes.Role,
        ValidateLifetime = true,
        ClockSkew = TimeSpan.FromMinutes(1)
    };

    private static string GenerateAndPersistSecret()
    {
        var configDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".dna");
        var secretFile = Path.Combine(configDir, ".jwt-secret");

        if (File.Exists(secretFile))
        {
            var existing = File.ReadAllText(secretFile).Trim();
            if (existing.Length >= 32) return existing;
        }

        Directory.CreateDirectory(configDir);
        var secret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
        File.WriteAllText(secretFile, secret);
        return secret;
    }
}
