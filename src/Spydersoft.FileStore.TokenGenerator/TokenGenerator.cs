using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.IdentityModel.Tokens;

namespace Spydersoft.FileStore.TokenGenerator;

public static class TokenGenerator
{
    public const string TestUserId = "filestore-test-user";

    public static string Generate(string base64Key, bool readOnly = false)
    {
        var key = new SymmetricSecurityKey(Convert.FromBase64String(base64Key));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, TestUserId),
            new("scope", "filestore:read"),
        };

        if (!readOnly)
            claims.Add(new("scope", "filestore:write"));

        var token = new JwtSecurityToken(
            claims: claims,
            expires: DateTime.UtcNow.AddDays(365),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
