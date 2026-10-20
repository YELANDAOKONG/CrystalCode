using System.Text.Json.Nodes;

using CrystalCode.Engine.Configuration;
using CrystalCode.Engine.Events;
using CrystalCode.Engine.Home;

namespace CrystalCode.Engine.Sessions;

public sealed partial class CodingSession
{
    private async Task ShowChildAsync(string argument, CancellationToken cancellationToken)
    {
        if (_frontEnd.ChooseChildAsync is null)
        {
            Error("Child inspection requires an interactive terminal.");
            return;
        }
        if (argument.Trim() == "parent")
        {
            Publish(new ChildViewRequested(null));
            return;
        }
        var children = _children.Snapshot();
        if (children.Count == 0)
        {
            Note("This session has no child sessions.");
            return;
        }
        var id = argument.Trim();
        if (id.Length == 0)
        {
            await _approvalQueue.WaitAsync(cancellationToken);
            try
            {
                id = await _frontEnd.ChooseChildAsync(children, cancellationToken);
            }
            finally
            {
                _approvalQueue.Release();
            }
            if (id is null)
            {
                return;
            }
        }
        var matches = children.Where(child => child.Id.StartsWith(id, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (matches.Length != 1)
        {
            Error(matches.Length == 0 ? "No matching child session." : "The child session prefix is ambiguous.");
            return;
        }
        Publish(new ChildSessionChanged(matches[0], Guid.Empty));
        Publish(new ChildViewRequested(matches[0].Id));
    }

    private IReadOnlyList<SlashCompletion> AgentCompletions()
    {
        var catalog = AgentCatalog.Load(_home, _workspace.Root, _settings.Agents with { Enabled = true });
        var commands = new List<SlashCompletion>
        {
            new("on", "Enable child Agents", ["on"]),
            new("off", "Disable child Agents", ["off"]),
            new("concurrency", "Concurrent child sessions", ["concurrency"],
                Enumerable.Range(1, 8).Select(value => new SlashCompletion(value.ToString(), "Child concurrency", [value.ToString()])).ToArray())
        };
        foreach (var command in new[] { "model", "thinking", "context", "tier", "speed" })
        {
            var names = new List<SlashCompletion>();
            foreach (var definition in catalog.Definitions)
            {
                IReadOnlyList<SlashCompletion> choices = [];
                try
                {
                    var selected = ResolveAgentModel(definition.Model);
                    choices = command switch
                    {
                        "model" => _settings.Catalog.Providers.Keys.Select(provider => new SlashCompletion(provider,
                            "Agent provider", [provider], _settings.Catalog.GetModelNames(new ProviderName(provider))
                                .Select(model => new SlashCompletion(model, "Agent model", [model])).ToArray())).ToArray(),
                        "thinking" => ThinkingCompletions.For(selected.ActiveModel),
                        "context" => ContextWindowSelection.Completions(selected.ActiveModel),
                        "tier" => CatalogSlotCompletions.For(selected.ActiveModel.ServiceTiers),
                        "speed" => CatalogSlotCompletions.For(selected.ActiveModel.Speeds),
                        _ => []
                    };
                }
                catch (Exception exception) when (exception is ArgumentException or KeyNotFoundException or InvalidOperationException)
                {
                    // The Agent stays listed so its invalid model choice can be repaired.
                }
                names.Add(new SlashCompletion(definition.Name, definition.Description, [definition.Name], choices, ArgumentsOptional: true));
            }
            commands.Add(new SlashCompletion(command, "Set or clear Agent " + command, [command], names));
        }
        return commands;
    }

    private void ChangeAgents(string argument)
    {
        try
        {
            ChangeAgentsCore(argument);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or KeyNotFoundException
            or IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            Error(exception.Message);
        }
    }

    private void ChangeAgentsCore(string argument)
    {
        var parts = CommandArguments.Split(argument);
        if (parts.Count == 0)
        {
            Note($"Agents {(_settings.Agents.Enabled ? "on" : "off")}  Concurrency {_settings.Agents.MaxConcurrent}");
            var catalog = AgentCatalog.Load(_home, _workspace.Root, _settings.Agents with { Enabled = true });
            foreach (var note in catalog.Notes)
            {
                Note(note);
            }
            foreach (var agent in catalog.Definitions)
            {
                Note($"{agent.Name}  {agent.Model.Provider ?? "inherit"}/{agent.Model.Model ?? "inherit"}  {agent.Description}");
            }
            return;
        }
        if (_turnActive || _compacting)
        {
            Error("Agent settings cannot change during a turn or compaction.");
            return;
        }
        var settings = _settings.Agents;
        if (parts.Count == 1 && parts[0] is "on" or "off")
        {
            settings = settings with { Enabled = parts[0] == "on" };
            SaveSettings(current => current.WithAgents(current.Agents with { Enabled = settings.Enabled }));
        }
        else if (parts.Count == 2 && parts[0] == "concurrency" && int.TryParse(parts[1], out var concurrency))
        {
            settings = settings with { MaxConcurrent = concurrency };
            SaveSettings(current => current.WithAgents(current.Agents with { MaxConcurrent = concurrency }));
        }
        else if (parts.Count >= 2 && parts[0] is "model" or "thinking" or "context" or "tier" or "speed")
        {
            var definition = AgentCatalog.Load(_home, _workspace.Root, settings with { Enabled = true })
                .Definitions.FirstOrDefault(agent => agent.Name == parts[1]);
            if (definition is null)
            {
                Error("Unknown or disabled Agent.");
                return;
            }
            var value = parts.Count > 2 ? parts[2] : null;
            if (parts[0] == "model" && parts.Count is not (2 or 4))
            {
                Error("Use /agents model <name> [provider model].");
                return;
            }
            if (parts[0] != "model" && parts.Count > 3)
            {
                Error("Use /agents <thinking|context|tier|speed> <name> [value].");
                return;
            }
            var model = definition.Model;
            if (parts[0] != "model" && (model.Provider is null || model.Model is null) && value is not null)
            {
                Error("Select an Agent model before setting its model-specific options.");
                return;
            }
            model = parts[0] switch
            {
                "model" => new AgentModelSettings { Provider = value, Model = parts.Count == 4 ? parts[3] : null },
                "thinking" => model with { Thinking = value is null ? null : ThinkingSelection.Parse(value).Value },
                "context" => model with { Context = value is null ? null : ContextWindowSelection.TryParse(value, out var window)
                    ? window : throw new ArgumentException("Invalid Agent context window.", nameof(argument)) },
                "tier" => model with { Tier = value },
                "speed" => model with { Speed = value },
                _ => throw new ArgumentException("Unknown Agent setting.", nameof(argument))
            };
            var resolved = ResolveAgentModel(model);
            if (parts[0] == "thinking" && value is not null && value != "default"
                && !ThinkingSelection.ChoicesFor(resolved.ActiveModel).Any(gear => gear.Value == model.Thinking))
            {
                Error("This thinking gear is not available for the Agent model.");
                return;
            }
            if (model.Context is int budget && !resolved.ActiveModel.ContextWindows.Contains(budget)
                || model.Tier is string tier && !resolved.ActiveModel.ServiceTiers.Contains(tier)
                || model.Speed is string speed && !resolved.ActiveModel.Speeds.Contains(speed))
            {
                Error("This option is not available for the Agent model.");
                return;
            }
            if (parts[0] == "model" && value is not null)
            {
                _ = GetChildClients(resolved);
            }
            if (definition.ManifestPath is string path)
            {
                var manifest = JsonNode.Parse(File.ReadAllText(path))?.AsObject()
                    ?? throw new InvalidDataException("Agent manifest is invalid.");
                manifest[parts[0] == "model" ? "provider" : parts[0]] = parts[0] == "model" ? model.Provider
                    : parts[0] == "context" ? JsonValue.Create(model.Context) : JsonValue.Create(value);
                if (parts[0] == "model")
                {
                    manifest["model"] = model.Model;
                    manifest.Remove("thinking");
                    manifest.Remove("context");
                    manifest.Remove("tier");
                    manifest.Remove("speed");
                    manifest["thinking"] = model.Thinking;
                }
                var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    File.WriteAllText(temporary, manifest.ToJsonString(HomeJson.Options));
                    File.Move(temporary, path, true);
                }
                finally
                {
                    File.Delete(temporary);
                }
            }
            else
            {
                SaveSettings(current => current.WithAgents(current.Agents.WithModel(definition.Name, model)));
            }
        }
        else
        {
            Error("Use /agents [on|off|concurrency <1-8>|model <name> [provider model]|thinking|context|tier|speed <name> [value]].");
            return;
        }
        RebuildExecutors();
        RefreshSlashCommands();
        Note("Agent settings updated.");
    }
}
