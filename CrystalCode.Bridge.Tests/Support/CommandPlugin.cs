using CrystalCode.Plugins;
using CrystalCode.Plugins.Commands.Interfaces;
using CrystalCode.Plugins.Interfaces;

namespace CrystalCode.Bridge.Tests.Support;

public sealed class CommandPlugin : IPlugin
{
    public string Name => "command-overrides";

    public PluginContribution Contribute() => new(commands:
        new[] { "edit", "btw", "child", "resume", "quick", "side", "editor", "continue", "shortcuts" }
            .Select(name => new Command(name)));

    private sealed class Command(string name) : IPluginCommand
    {
        public string Name => name;

        public string Help => "Plugin " + name;

        public bool OverridesBuiltIn => true;

        public void Execute(string argument, IPluginOutput output) => output.Write("Plugin handled " + name);
    }
}
