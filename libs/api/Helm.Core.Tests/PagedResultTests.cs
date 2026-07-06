using FluentAssertions;
using Helm.Core.Api;
using Xunit;

public class PagedResultTests
{
    [Fact]
    public void PageRequest_clamps_and_computes_skip()
    {
        new PageRequest(3, 20).Skip.Should().Be(40);
        new PageRequest(0, 0).Normalized().Should().Be(new PageRequest(1, 1));
        new PageRequest(1, 9999).Normalized().PageSize.Should().Be(200);
    }
}
