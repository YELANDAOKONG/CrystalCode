using Xunit;

namespace CrystalCode.Display.Tests.Paint;

/// <summary>
/// Theme holds process-wide state, so tests that change it must not overlap other tests.
/// </summary>
[CollectionDefinition("Theme", DisableParallelization = true)]
public sealed class ThemeCollection;
