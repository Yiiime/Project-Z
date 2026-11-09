namespace Seabreeze.Editor
{
    public static class MemoryIntegrationVerification
    {
        // Existing CI/editor entry delegates to the current eight-role runtime check.
        public static void Begin() { UnifiedRuntimeVerification.Begin(); }
    }
}
