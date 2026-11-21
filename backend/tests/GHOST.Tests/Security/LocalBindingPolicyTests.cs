using GHOST.API.Configuration;

namespace GHOST.Tests.Security;

public sealed class LocalBindingPolicyTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("http://localhost:5153")]
    [InlineData("http://127.0.0.1:5153")]
    [InlineData("http://[::1]:5153")]
    [InlineData("http://localhost:5153;https://127.0.0.1:7179")]
    public void Loopback_is_accepted(string? urls) => LocalBindingPolicy.Validate(urls);

    [Theory]
    [InlineData("http://*:5153")]
    [InlineData("http://+:5153")]
    [InlineData("http://0.0.0.0:5153")]
    [InlineData("http://[::]:5153")]
    [InlineData("http://192.168.1.20:5153")]
    [InlineData("http://myhost:5153")]
    [InlineData("http://localhost:5153;http://192.168.1.20:5153")]
    public void Network_addresses_are_refused(string urls)
        => Assert.Throws<InvalidOperationException>(() => LocalBindingPolicy.Validate(urls));
}
