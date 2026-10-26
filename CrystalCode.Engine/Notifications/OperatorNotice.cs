using CrystalCode.Engine.Configuration;

namespace CrystalCode.Engine.Notifications;

/// <summary>
/// One reason the session waits for the operator, with the plain-text fields
/// the notification command receives. <see cref="Fields"/> keys are variable
/// name suffixes after <c>CRYSTAL_NOTIFY_</c>.
/// </summary>
internal sealed record OperatorNotice(
    OperatorNotifyEvent Event,
    string Text,
    IReadOnlyList<KeyValuePair<string, string>> Fields);
