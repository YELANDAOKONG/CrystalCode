using CrystalCode.Engine.Configuration;
using CrystalCode.Engine.Events;
using CrystalCode.Engine.Plugins;
using CrystalCode.Plugins.Status;
using CrystalCode.Plugins.Status.Interfaces;

using Xunit;

namespace CrystalCode.Engine.Tests.Plugins;

public sealed class PluginStatusFieldTests
{
    [Fact]
    public void TryAdmit_RejectsHostNamesCommandsAndDuplicates()
    {
        var accepted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var notes = new List<string>();

        Assert.True(PluginStatusFieldAdmission.TryAdmit(
            "Acme",
            new Named("Git-Branch", "Current branch"),
            accepted,
            notes,
            out var first));
        Assert.False(PluginStatusFieldAdmission.TryAdmit(
            "Other",
            new Named("git-branch", "Again"),
            accepted,
            notes,
            out _));
        Assert.False(PluginStatusFieldAdmission.TryAdmit(
            "Other",
            new Named("model", "Nope"),
            accepted,
            notes,
            out _));
        Assert.False(PluginStatusFieldAdmission.TryAdmit(
            "Other",
            new Named("reset", "Nope"),
            accepted,
            notes,
            out _));
        Assert.False(PluginStatusFieldAdmission.TryAdmit(
            "Other",
            new Named("not--name", "Nope"),
            accepted,
            notes,
            out _));

        Assert.Equal("git-branch", first!.Name);
        Assert.Equal("Current branch", first.Description);
        Assert.Contains(notes, note => note.Contains("duplicate", StringComparison.Ordinal));
        Assert.Contains(notes, note => note.Contains("host owns", StringComparison.Ordinal));
        Assert.Contains(notes, note => note.Contains("not--name", StringComparison.Ordinal));
    }

    [Fact]
    public void Normalize_CollapsesToOnePlainLine()
    {
        var text = PluginStatusText.Normalize("  \u001b[31mleft\n\tright  ", 64);

        Assert.Equal("left  right", text);
        Assert.Equal(string.Empty, PluginStatusText.Normalize(" \n\t ", 64));
        Assert.Equal(64, PluginStatusText.Normalize(new string('a', 80), 64).Length);
    }

    [Fact]
    public void Board_PublishesChangesAndIgnoresARetiredSink()
    {
        var published = new List<IReadOnlyList<StatusFieldValue>>();
        var notes = new List<string>();
        var board = new PluginStatusBoard(
            fields => published.Add(fields),
            notes.Add);
        var sink = board.Open("Acme", ["branch"]);

        sink.Set("branch", "main");
        sink.Set("branch", "main");
        sink.Set("model", "nope");
        sink.Set("model", "still nope");
        sink.Clear("branch");

        Assert.Equal(2, published.Count);
        Assert.Equal("main", Assert.Single(published[0]).Text);
        Assert.Empty(published[1]);
        Assert.Equal(
            ["Plugin 'Acme' ignored status field 'model'."],
            notes);

        board.Retire();
        sink.Set("branch", "later");

        Assert.Equal(2, published.Count);
    }

    [Fact]
    public async Task Board_DeliversSnapshotsInTheOrderTheyWereTaken()
    {
        var published = new List<string>();
        var entered = new ManualResetEventSlim(false);
        var release = new ManualResetEventSlim(false);
        var board = new PluginStatusBoard(
            fields =>
            {
                var text = fields.Count == 0 ? string.Empty : fields[0].Text;
                if (text == "one")
                {
                    entered.Set();
                    Assert.True(release.Wait(TimeSpan.FromSeconds(5)));
                }

                lock (published)
                {
                    published.Add(text);
                }
            },
            static _ => { });
        var sink = board.Open("Acme", ["branch"]);
        var first = Task.Run(() => sink.Set("branch", "one"));
        Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));

        var second = Task.Run(() => sink.Set("branch", "two"));
        await second.WaitAsync(TimeSpan.FromSeconds(5));
        release.Set();
        await first.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(["one", "two"], published);
    }

    [Fact]
    public async Task Board_ClearFromRetireStaysAfterAnInFlightSnapshot()
    {
        var published = new List<string>();
        var entered = new ManualResetEventSlim(false);
        var release = new ManualResetEventSlim(false);
        var board = new PluginStatusBoard(
            fields =>
            {
                var text = fields.Count == 0 ? string.Empty : fields[0].Text;
                if (text == "one")
                {
                    entered.Set();
                    Assert.True(release.Wait(TimeSpan.FromSeconds(5)));
                }

                lock (published)
                {
                    published.Add(text);
                }
            },
            static _ => { });
        var sink = board.Open("Acme", ["branch"]);
        var first = Task.Run(() => sink.Set("branch", "one"));
        Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));

        board.Retire();
        release.Set();
        await first.WaitAsync(TimeSpan.FromSeconds(5));
        sink.Set("branch", "later");

        Assert.Equal(["one", string.Empty], published);
    }

    [Fact]
    public void Close_DropsTextThatWasPublishedBeforeAttachFailed()
    {
        var published = new List<IReadOnlyList<StatusFieldValue>>();
        var board = new PluginStatusBoard(
            fields => published.Add(fields),
            static _ => { });
        var branch = board.Open("Acme", ["branch"]);
        var ahead = board.Open("Other", ["ahead"]);
        branch.Set("branch", "main");
        ahead.Set("ahead", "1");

        board.Close(branch);
        branch.Set("branch", "later");

        var last = published[^1];
        var value = Assert.Single(last);
        Assert.Equal("ahead", value.Name);
        Assert.Equal("1", value.Text);
    }

    [Fact]
    public void Settings_KeepUnloadedNamesAndCommandsRejectThem()
    {
        var stored = new StatusLineSettings(true, ["model", "branch"]);
        Assert.Equal(["model", "branch"], stored.Fields);

        var admitted = new HashSet<string>(StringComparer.Ordinal) { "branch" };
        var selected = stored.WithFields(["model", "branch"], admitted);
        Assert.Equal(["model", "branch"], selected.Fields);

        var error = Assert.Throws<ArgumentException>(
            () => stored.WithFields(["model", "missing"], admitted));
        Assert.Contains("Unknown status line field  missing", error.Message, StringComparison.Ordinal);
    }

    private sealed class Named : IPluginStatusField
    {
        public Named(string name, string description)
        {
            Name = name;
            Description = description;
        }

        public string Name { get; }

        public string Description { get; }
    }
}
