using Spectre.Console;
using Spectre.Console.Rendering;

using CrystalCode.Display.Paint;
using CrystalCode.Engine.Sessions;

namespace CrystalCode.Terminal;

/// <summary>Asks whether to trust a directory inside the terminal shell.</summary>
internal sealed class TrustPrompt : IWorkspaceTrustPrompt
{
    private readonly SessionRenderer _renderer;

    public TrustPrompt(SessionRenderer renderer)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        _renderer = renderer;
    }

    public async ValueTask<bool> ConfirmAsync(
        WorkspaceTrustRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var selected = 0;
        _renderer.CloseStream();
        _renderer.PauseComposer();
        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                _renderer.SetOverlay(Render(request, selected));
                var key = await _renderer.ReadKeyAsync(
                    scrollPlainArrows: false,
                    cancellationToken);
                switch (key.Key)
                {
                    case ConsoleKey.Escape:
                        return false;
                    case ConsoleKey.Enter:
                        return selected == 0;
                    case ConsoleKey.UpArrow:
                        selected = 0;
                        break;
                    case ConsoleKey.DownArrow:
                        selected = 1;
                        break;
                    case ConsoleKey.Y:
                        return true;
                    case ConsoleKey.N:
                        return false;
                    default:
                        break;
                }
            }
        }
        finally
        {
            _renderer.ClearOverlay();
            _renderer.ResumeComposer();
        }
    }

    private static IRenderable Render(WorkspaceTrustRequest request, int selected)
    {
        var blocks = new List<IRenderable>
        {
            new Markup($"[{Theme.Accent} bold]Trust this directory?[/]"),
            new Markup(MarkupText.Escape(request.TrustRoot))
        };
        if (request.CoversRepository)
        {
            blocks.Add(new Markup(
                $"[{Theme.Muted}]Workspace[/]  {MarkupText.Escape(request.Workspace)}"));
            blocks.Add(new Markup(
                "This is the git root of the current workspace. Trusting it lets Crystal Code load project instructions, prompts, tools, and plugins from the repository."));
        }
        else
        {
            blocks.Add(new Markup(
                "Trusting it lets Crystal Code load project instructions, prompts, tools, and plugins from this directory."));
        }

        blocks.Add(Choice(selected == 0, "Yes"));
        blocks.Add(Choice(selected == 1, "No"));
        blocks.Add(new Markup($"[{Theme.Muted}]Enter confirms  Esc cancels[/]"));
        var panel = new Panel(new Rows(blocks))
        {
            Header = new PanelHeader("Trust"),
            Border = BoxBorder.Rounded,
            BorderStyle = Style.Parse(Theme.Chrome),
            Padding = new Padding(1, 0, 1, 0),
            Expand = true
        };
        return new Padder(panel, new Padding(2, 0, 0, 0));
    }

    private static IRenderable Choice(bool selected, string label)
    {
        var mark = selected ? ">" : " ";
        var style = selected ? Theme.Accent + " bold" : Theme.Muted;
        return new Markup($"[{style}]{mark} {label}[/]");
    }
}
