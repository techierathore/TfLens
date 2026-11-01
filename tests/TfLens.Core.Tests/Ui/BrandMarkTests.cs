using FluentAssertions;
using TfLens.Services.Ui;

namespace TfLens.Core.Tests.Ui;

/// <summary>
/// The absolute share-image URL in the head of every page (REQ-UI-073, BRD-207).
/// </summary>
public sealed class BrandMarkTests
{
    /// <summary>
    /// On localhost the share image is the request's scheme and host followed by the plain brand path.
    /// </summary>
    [Fact]
    public void ShareUrlUsesRequestHost()
    {
        BrandMark.ShareImageUrl("http", "localhost:5099", string.Empty)
            .Should().Be("http://localhost:5099/brand/TfLens.png");
    }

    /// <summary>
    /// Behind the reverse proxy the forwarded https scheme and the public host are what the URL carries.
    /// </summary>
    [Fact]
    public void ShareUrlFollowsProxyScheme()
    {
        BrandMark.ShareImageUrl("https", "tflens.example.org", null)
            .Should().Be("https://tflens.example.org/brand/TfLens.png");
    }

    /// <summary>
    /// An app served under a path base keeps that base in front of the brand path, with no doubled slash.
    /// </summary>
    [Fact]
    public void ShareUrlKeepsPathBase()
    {
        BrandMark.ShareImageUrl("https", "host", "/lens/")
            .Should().Be("https://host/lens/brand/TfLens.png");
    }

    /// <summary>
    /// A blank host cannot make an absolute URL, so it is refused rather than emitting "https:///brand/…".
    /// </summary>
    [Fact]
    public void ShareUrlRefusesBlankHost()
    {
        var vAct = () => BrandMark.ShareImageUrl("https", " ", null);

        vAct.Should().Throw<ArgumentException>();
    }
}
