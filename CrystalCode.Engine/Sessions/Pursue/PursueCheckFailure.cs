namespace CrystalCode.Engine.Sessions.Pursue;

/// <summary>Why a checker call produced no usable verdict.</summary>
internal enum PursueCheckFailure
{
    None,
    Request,
    Invalid,
    Overflow
}
