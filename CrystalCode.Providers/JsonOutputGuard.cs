using Crystal;

namespace CrystalCode.Providers;

internal static class JsonOutputGuard
{
    public static void Reject(JsonOutputRequirement? requirement, string vendorName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(vendorName);
        if (requirement is null)
        {
            return;
        }

        throw new NotSupportedException(
            $"{vendorName} does not support a JSON output schema.");
    }
}
