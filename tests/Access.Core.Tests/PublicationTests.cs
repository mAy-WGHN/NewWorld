using Access.Core.Publishing;

namespace Access.Core.Tests;

public class PublicationTests
{
    [Fact]
    public void First_state_is_published_as_version_one() =>
        Publication.Next(previous: null, fingerprint: "a").ShouldBe(new Publication(1, "a"));

    [Fact]
    public void Unchanged_state_is_not_published_again() =>
        Publication.Next(new Publication(4, "a"), "a").ShouldBeNull();

    [Fact]
    public void Versions_rise_strictly_even_when_a_state_returns()
    {
        var v1 = Publication.Next(null, "a")!;
        var v2 = Publication.Next(v1, "b")!;
        var v3 = Publication.Next(v2, "a")!;

        new[] { v1.Version, v2.Version, v3.Version }.ShouldBe([1L, 2L, 3L]);
    }

    [Fact]
    public void Fingerprint_does_not_depend_on_order()
    {
        var one = Fingerprint.Of(new Dictionary<string, string[]> { ["b"] = ["2", "1"], ["a"] = ["x"] });
        var two = Fingerprint.Of(new Dictionary<string, string[]> { ["a"] = ["x"], ["b"] = ["1", "2"] });

        one.ShouldBe(two);
        Fingerprint.Of(new Dictionary<string, string[]> { ["a"] = ["y"] }).ShouldNotBe(one);
    }
}
