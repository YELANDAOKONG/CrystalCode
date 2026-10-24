using System.Text.Json;

using CrystalCode.Engine.Mcp;
using CrystalCode.Engine.Tools;

using ModelContextProtocol.Protocol;

using Xunit;

namespace CrystalCode.Engine.Tests.Mcp;

public sealed class McpHostCallbackTests
{
    [Fact]
    public async Task Roots_AnswerWithTheCurrentWorkspace()
    {
        var root = Path.Combine(Path.GetTempPath(), "crystal-mcp-roots", "project");
        var current = root;
        await using var server = await StartAsync(new Callbacks(() => current));

#pragma warning disable MCP9005
        var first = await server.Server.RequestRootsAsync(new ListRootsRequestParams());
        current = Path.Combine(Path.GetTempPath(), "crystal-mcp-roots", "other");
        var second = await server.Server.RequestRootsAsync(new ListRootsRequestParams());
#pragma warning restore MCP9005

        var firstRoot = Assert.Single(first.Roots);
        Assert.Equal(new Uri(Path.GetFullPath(root)).AbsoluteUri, firstRoot.Uri);
        Assert.Equal("project", firstRoot.Name);
        Assert.Equal(new Uri(Path.GetFullPath(current)).AbsoluteUri, Assert.Single(second.Roots).Uri);
    }

    [Fact]
    public async Task Client_DeclaresRootsAndFormElicitation_ButNotSampling()
    {
        await using var server = await StartAsync(new Callbacks(() => "/work"));

        var capabilities = server.Server.ClientCapabilities!;

#pragma warning disable MCP9005
        Assert.NotNull(capabilities.Roots);
#pragma warning restore MCP9005
        Assert.NotNull(capabilities.Elicitation?.Form);
        Assert.Null(capabilities.Elicitation?.Url);
        Assert.Null(capabilities.Sampling);
    }

    [Fact]
    public async Task Elicitation_AsksTheOperatorAndReturnsTheirAnswers()
    {
        var prompt = new ScriptedPrompt(new QuestionResponse([["Alice"], ["42"], ["Yes"], ["blue"]], false));
        await using var server = await StartAsync(new Callbacks(() => "/work", prompt));

        var result = await server.Server.ElicitAsync(Form(), CancellationToken.None);

        Assert.Equal("accept", result.Action);
        Assert.Equal("Alice", result.Content!["name"].GetString());
        Assert.Equal(42, result.Content["age"].GetInt64());
        Assert.True(result.Content["subscribe"].GetBoolean());
        Assert.Equal("blue", result.Content["color"].GetString());
        var asked = Assert.Single(prompt.Asked);
        Assert.Equal(4, asked.Count);
        Assert.All(asked, question => Assert.StartsWith("MCP test-server-name", question.Header, StringComparison.Ordinal));
        Assert.Contains("Pick your settings", asked[0].Text, StringComparison.Ordinal);
        Assert.Equal(["red", "blue"], asked[3].Options.Select(option => option.Label).ToArray());
        Assert.False(asked[3].Custom);
    }

    [Fact]
    public async Task Elicitation_CancelsWhenTheOperatorRejects()
    {
        var prompt = new ScriptedPrompt(new QuestionResponse([], true));
        await using var server = await StartAsync(new Callbacks(() => "/work", prompt));

        var result = await server.Server.ElicitAsync(Form(), CancellationToken.None);

        Assert.Equal("cancel", result.Action);
        Assert.Null(result.Content);
    }

    [Fact]
    public async Task Elicitation_DeclinesWhenAnAnswerDoesNotFitTheField()
    {
        var prompt = new ScriptedPrompt(new QuestionResponse([["Alice"], ["not a number"], ["Yes"], ["blue"]], false));
        await using var server = await StartAsync(new Callbacks(() => "/work", prompt));

        var result = await server.Server.ElicitAsync(Form(), CancellationToken.None);

        Assert.Equal("decline", result.Action);
    }

    [Fact]
    public async Task Elicitation_SkipsAnEmptyOptionalField()
    {
        var prompt = new ScriptedPrompt(new QuestionResponse([["Alice"], [], ["No"], ["red"]], false));
        await using var server = await StartAsync(new Callbacks(() => "/work", prompt));

        var result = await server.Server.ElicitAsync(Form(), CancellationToken.None);

        Assert.Equal("accept", result.Action);
        Assert.False(result.Content!.ContainsKey("age"));
        Assert.False(result.Content["subscribe"].GetBoolean());
    }

    [Fact]
    public async Task Elicitation_DeclinesAFieldTypeItCannotExpress()
    {
        var prompt = new ScriptedPrompt(new QuestionResponse([["x"]], false));
        await using var server = await StartAsync(new Callbacks(() => "/work", prompt));
        var request = new ElicitRequestParams
        {
            Message = "Pick several",
            RequestedSchema = new ElicitRequestParams.RequestSchema
            {
                Properties = new Dictionary<string, ElicitRequestParams.PrimitiveSchemaDefinition>
                {
                    ["tags"] = new ElicitRequestParams.UntitledMultiSelectEnumSchema
                    {
                        Items = new ElicitRequestParams.UntitledEnumItemsSchema { Enum = ["a", "b"] }
                    }
                }
            }
        };

        var result = await server.Server.ElicitAsync(request, CancellationToken.None);

        Assert.Equal("decline", result.Action);
        Assert.Empty(prompt.Asked);
    }

    [Fact]
    public async Task Elicitation_IsDeclinedWithoutAnOperator()
    {
        await using var server = await StartAsync(McpHostCallbacks.None);

        var result = await server.Server.ElicitAsync(Form(), CancellationToken.None);

        Assert.Equal("cancel", result.Action);
    }

    [Fact]
    public async Task ToolListChanged_ReachesTheSessionCallback()
    {
        var changed = new List<string>();
        var completed = new TaskCompletionSource();
        await using var server = await StartAsync(new McpHostCallbacks(
            () => "/work",
            new ScriptedPrompt(new QuestionResponse([], true)),
            name =>
            {
                changed.Add(name);
                completed.TrySetResult();
            }));

        await server.Server.SendNotificationAsync(
            NotificationMethods.ToolListChangedNotification,
            CancellationToken.None);
        await completed.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(["test-server-name"], changed);
    }

    private static ElicitRequestParams Form() =>
        new()
        {
            Message = "Pick your settings",
            RequestedSchema = new ElicitRequestParams.RequestSchema
            {
                Properties = new Dictionary<string, ElicitRequestParams.PrimitiveSchemaDefinition>
                {
                    ["name"] = new ElicitRequestParams.StringSchema { Title = "Name" },
                    ["age"] = new ElicitRequestParams.NumberSchema { Type = "integer", Title = "Age" },
                    ["subscribe"] = new ElicitRequestParams.BooleanSchema { Title = "Subscribe" },
                    ["color"] = new ElicitRequestParams.UntitledSingleSelectEnumSchema
                    {
                        Title = "Color",
                        Enum = ["red", "blue"]
                    }
                },
                Required = ["name", "subscribe", "color"]
            }
        };

    private static Task<InProcessMcpServer> StartAsync(McpHostCallbacks callbacks)
    {
        var definition = new McpServerDefinition(
            "test-server-name",
            McpServerSource.Home,
            McpTransportKind.Stdio,
            command: "unused");
        return InProcessMcpServer.StartWithHostAsync(new InProcessMcpServer.Features(), definition, callbacks);
    }

    private sealed class Callbacks
    {
        private readonly Func<string> _root;
        private readonly IUserPrompt _prompt;

        public Callbacks(Func<string> root, IUserPrompt? prompt = null)
        {
            _root = root;
            _prompt = prompt ?? new ScriptedPrompt(new QuestionResponse([], true));
        }

        public static implicit operator McpHostCallbacks(Callbacks callbacks) =>
            new(callbacks._root, callbacks._prompt, static _ => { });
    }

    private sealed class ScriptedPrompt(QuestionResponse response) : IUserPrompt
    {
        public List<IReadOnlyList<UserQuestion>> Asked { get; } = [];

        public ValueTask<QuestionResponse> AskAsync(
            IReadOnlyList<UserQuestion> questions,
            CancellationToken cancellationToken = default)
        {
            Asked.Add(questions);
            return ValueTask.FromResult(response);
        }
    }
}
