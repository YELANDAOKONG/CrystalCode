using Crystal.Chat;
using Crystal.Tools;

using CrystalCode.Engine.Configuration;
using CrystalCode.Engine.Sessions;

using Xunit;

namespace CrystalCode.Engine.Tests.Sessions;

public sealed class ResourceTrimmerTests
{
    private static readonly byte[] PngSignature =
        [0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a];

    private static ResourceTrimSettings Settings(
        int keep = 2,
        int slack = 1,
        long maxBytes = 0,
        bool includeUser = false,
        bool enabled = true) =>
        new(enabled, keep, slack, maxBytes, includeUser);

    private static ImageAttachment Image(int number, int size = 8) =>
        new(number, "image/png", PngSignature.Concat(new byte[size - PngSignature.Length]).ToArray());

    private static Dictionary<int, ImageAttachment> Images(int count, int size = 8) =>
        Enumerable.Range(1, count).ToDictionary(number => number, number => Image(number, size));

    private static List<ChatItem> ToolTranscript(int count) =>
    [
        .. Enumerable.Range(1, count).Select(number =>
            (ChatItem)new ToolResult("c" + number, ImageMarkerText.Tag(number), ToolResultStatus.Success))
    ];

    private static List<ChatItem> UserThenTools() =>
    [
        new ChatMessage(ChatRole.User, ImageMarkerText.Tag(1)),
        .. ToolTranscript(5).Skip(1)
    ];

    [Fact]
    public void Apply_WithinKeepPlusSlack_KeepsEveryImage()
    {
        var trimmer = new ResourceTrimmer();

        var visible = trimmer.Apply(ToolTranscript(3), Images(3), Settings());

        Assert.Equal([1, 2, 3], visible.Keys.Order());
    }

    [Fact]
    public void Apply_BeyondSlack_KeepsOnlyTheNewestAndStaysTrimmed()
    {
        var trimmer = new ResourceTrimmer();
        var images = Images(4);
        var transcript = ToolTranscript(4);

        Assert.Equal([3, 4], trimmer.Apply(transcript, images, Settings()).Keys.Order());

        images.Add(5, Image(5));
        transcript.AddRange(ToolTranscript(5).Skip(4));

        Assert.Equal([3, 4, 5], trimmer.Apply(transcript, images, Settings()).Keys.Order());
    }

    [Fact]
    public void Apply_DoesNotTrimUserImagesByDefault()
    {
        var trimmer = new ResourceTrimmer();
        var transcript = UserThenTools();

        var visible = trimmer.Apply(transcript, Images(5), Settings());

        Assert.Equal([1, 4, 5], visible.Keys.Order());
    }

    [Fact]
    public void Apply_IncludeUserResources_TrimsThemToo()
    {
        var trimmer = new ResourceTrimmer();
        var transcript = UserThenTools();

        var visible = trimmer.Apply(transcript, Images(5), Settings(includeUser: true));

        Assert.Equal([4, 5], visible.Keys.Order());
    }

    [Fact]
    public void Apply_ByteBudget_TrimsOldestFirstWithoutWaitingForSlack()
    {
        var trimmer = new ResourceTrimmer();

        var visible = trimmer.Apply(
            ToolTranscript(3),
            Images(3, size: 100),
            Settings(keep: 5, slack: 5, maxBytes: 250));

        Assert.Equal([2, 3], visible.Keys.Order());
    }

    [Fact]
    public void Apply_ByteBudget_KeepsTheNewestImageEvenWhenItAloneExceedsTheBudget()
    {
        var trimmer = new ResourceTrimmer();

        var visible = trimmer.Apply(
            ToolTranscript(2),
            Images(2, size: 100),
            Settings(keep: 5, slack: 5, maxBytes: 10));

        Assert.Equal([2], visible.Keys.Order());
    }

    [Fact]
    public void Apply_Disabled_KeepsEverythingUntilATrim()
    {
        var trimmer = new ResourceTrimmer();
        var transcript = ToolTranscript(6);
        var images = Images(6);

        Assert.Equal(6, trimmer.Apply(transcript, images, Settings(enabled: false)).Count);

        var result = trimmer.Trim(transcript, images, keep: 1, includeUserResources: false);

        Assert.Equal(5, result.Resources);
        Assert.Equal(40, result.Bytes);
        Assert.Equal([6], trimmer.Apply(transcript, images, Settings(enabled: false)).Keys.Order());
    }

    [Fact]
    public void Trim_AllLeavesNoTrimmableImage()
    {
        var trimmer = new ResourceTrimmer();

        trimmer.Trim(ToolTranscript(2), Images(2), keep: 0, includeUserResources: false);

        Assert.Empty(trimmer.Apply(ToolTranscript(2), Images(2), Settings(enabled: false)));
    }

    [Fact]
    public void Reset_ForgetsEarlierOmissions()
    {
        var trimmer = new ResourceTrimmer();
        trimmer.Trim(ToolTranscript(2), Images(2), keep: 0, includeUserResources: false);

        trimmer.Reset();

        Assert.Equal(2, trimmer.Apply(ToolTranscript(2), Images(2), Settings(enabled: false)).Count);
    }

    [Theory]
    [InlineData("", null, null)]
    [InlineData("all", 0, null)]
    [InlineData("3", 3, null)]
    [InlineData("on", null, true)]
    [InlineData("OFF", null, false)]
    public void Arguments_Parse(string argument, int? keep, bool? enabled)
    {
        Assert.True(ResourceTrimArguments.TryParse(argument, out var request, out _));
        Assert.Equal(keep, request.Keep);
        Assert.Equal(enabled, request.Enabled);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("some")]
    public void Arguments_RejectOtherText(string argument)
    {
        Assert.False(ResourceTrimArguments.TryParse(argument, out _, out var error));
        Assert.NotEmpty(error);
    }
}
