using CrystalCode.Engine.Approvals.Interfaces;
using CrystalCode.Engine.Events;
using CrystalCode.Engine.Tools;

namespace CrystalCode.Engine.Sessions;

/// <summary>
/// Everything a front end supplies to a session: where events go, and who
/// answers approval requests, operator questions, and session choices.
/// </summary>
public sealed record SessionFrontEnd(
    ISessionObserver Observer,
    IApprovalPrompt Approvals,
    IUserPrompt Questions,
    ISessionChooser Sessions,
    IWorkspaceTrustPrompt Trust);
