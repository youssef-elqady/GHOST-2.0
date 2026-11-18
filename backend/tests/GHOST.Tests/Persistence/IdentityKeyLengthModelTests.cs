using GHOST.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace GHOST.Tests.Persistence;

public sealed class IdentityKeyLengthModelTests
{
    [Fact]
    public void IdentityCompositeKeyProperties_AreBoundedTo128Characters()
    {
        var options = new DbContextOptionsBuilder<GHOSTDbContext>()
            .UseSqlServer("Server=localhost;Database=GHOST_MODEL_ONLY;Trusted_Connection=True;TrustServerCertificate=True")
            .Options;

        using var context = new GHOSTDbContext(options);
        var model = context.Model;

        var login = model.FindEntityType(typeof(IdentityUserLogin<Guid>))!;
        Assert.Equal(128, login.FindProperty(nameof(IdentityUserLogin<Guid>.LoginProvider))!.GetMaxLength());
        Assert.Equal(128, login.FindProperty(nameof(IdentityUserLogin<Guid>.ProviderKey))!.GetMaxLength());

        var token = model.FindEntityType(typeof(IdentityUserToken<Guid>))!;
        Assert.Equal(128, token.FindProperty(nameof(IdentityUserToken<Guid>.LoginProvider))!.GetMaxLength());
        Assert.Equal(128, token.FindProperty(nameof(IdentityUserToken<Guid>.Name))!.GetMaxLength());

        var loginKeyBytes = login.FindPrimaryKey()!.Properties.Sum(property =>
            checked(property.GetMaxLength()!.Value * 2));
        var tokenKeyBytes = token.FindPrimaryKey()!.Properties.Sum(property =>
            property.ClrType == typeof(Guid)
                ? 16
                : checked(property.GetMaxLength()!.Value * 2));

        Assert.Equal(512, loginKeyBytes);
        Assert.Equal(528, tokenKeyBytes);
        Assert.True(loginKeyBytes <= 900);
        Assert.True(tokenKeyBytes <= 900);
    }
}
