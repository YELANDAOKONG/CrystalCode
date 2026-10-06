using CrystalCode.Engine.Plugins.Interfaces;
using CrystalCode.Plugins.Commands;

namespace CrystalCode.Engine.Plugins.Disk;

/// <summary>Adapts a disk slash command onto the host command table.</summary>
internal sealed class DiskCommand : ISlashCommand
{
    private readonly IPluginCommand _inner;

    public DiskCommand(IPluginCommand inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        Name = inner.Name.Trim();
        Help = string.IsNullOrWhiteSpace(inner.Help) ? Name : inner.Help.Trim();
        _inner = inner;
    }

    public string Name { get; }

    public string Help { get; }

    public void Execute(string argument, ISlashOutput output)
    {
        ArgumentNullException.ThrowIfNull(output);
        _inner.Execute(argument ?? string.Empty, new Output(output));
    }

    private sealed class Output : IPluginOutput
    {
        private readonly ISlashOutput _output;

        public Output(ISlashOutput output)
        {
            _output = output;
        }

        public void Write(string text) => _output.WriteNote(text ?? string.Empty);

        public void Fail(string text) => _output.WriteError(text ?? string.Empty);
    }
}
