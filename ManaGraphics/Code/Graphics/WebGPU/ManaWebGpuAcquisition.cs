namespace ManaGraphics.Code.Graphics;

internal static class ManaWebGpuAcquisition
{
    // Each acquired reference is a separate obligation, even when several slots share one texture.
    internal static T[] Acquire<T>(int count, Func<int, T> acquire, Action<T> release)
    {
        var resources = new T[count];
        var acquired = 0;
        try
        {
            for (; acquired < count; acquired++)
            {
                resources[acquired] = acquire(acquired);
            }
            return resources;
        }
        catch
        {
            for (var index = acquired - 1; index >= 0; index--)
            {
                release(resources[index]);
            }
            throw;
        }
    }
}
