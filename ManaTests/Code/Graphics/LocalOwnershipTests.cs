using ManaGraphics.Code.Graphics;
using ManaUI;
using ManaGraphics.Code.Graphics.Resources;

namespace ManaTests;

public static class LocalOwnershipChecks
{
    public static void Run(ManaFontManager fonts, Action<string, bool, string> check)
    {
        void Check(string name, bool passed) => check(name, passed, "");
        var shared = new object();
        var releases = new List<int>();
        var acquired = ManaWebGpuAcquisition.Acquire(6, index => index, releases.Add);
        Check("successful acquisition transfers all references without releasing them", acquired.SequenceEqual(Enumerable.Range(0, 6)) && releases.Count == 0);
        var failure = new IOException("allocation failed");
        Exception? observed = null;
        try
        {
            ManaWebGpuAcquisition.Acquire(6, index => index == 4 ? throw failure : index, releases.Add);
        }
        catch (IOException ex)
        {
            observed = ex;
        }
        Check("failed acquisition rolls back every earlier slot in reverse order", releases.SequenceEqual(new[] { 3, 2, 1, 0 }));
        Check("rollback preserves the original allocation error", ReferenceEquals(observed, failure));
        var sharedReleases = 0;
        try
        {
            ManaWebGpuAcquisition.Acquire(6, index => index == 5 ? throw failure : shared, _ => sharedReleases++);
        }
        catch (IOException) { }
        Check("shared neutral textures return every acquired reference on failure", sharedReleases == 5);
        var firstRelease = false;
        try
        {
            ManaWebGpuAcquisition.Acquire<object>(6, _ => throw failure, _ => firstRelease = true);
        }
        catch (IOException) { }
        Check("first-allocation failure releases no unowned resource", !firstRelease);

        var calls = 0;
        var first = ManaWebGpuInitializationRequests.Register(handle => { calls += handle; return true; });
        Check("initialization routes a result to its matching request", ManaWebGpuInitializationRequests.Complete(first, 7) && calls == 7);
        Check("duplicate initialization delivery cannot transfer the handle twice", !ManaWebGpuInitializationRequests.Complete(first, 7) && calls == 7);
        var canceled = ManaWebGpuInitializationRequests.Register(_ => { calls++; return true; });
        ManaWebGpuInitializationRequests.Cancel(canceled);
        ManaWebGpuInitializationRequests.Cancel(canceled);
        var freshCalls = 0;
        var fresh = ManaWebGpuInitializationRequests.Register(_ => { freshCalls++; return true; });
        Check("a canceled context's promise cannot route to a fresh context", !ManaWebGpuInitializationRequests.Complete(canceled, 9) && fresh != canceled && freshCalls == 0);
        Check("fresh initialization still accepts its own result", ManaWebGpuInitializationRequests.Complete(fresh, 9) && freshCalls == 1);
        var rejected = ManaWebGpuInitializationRequests.Register(_ => false);
        Check("a closing owner rejects the handle so JavaScript can release it", !ManaWebGpuInitializationRequests.Complete(rejected, 12));
        Check("rejected requests leave no callback route behind", !ManaWebGpuInitializationRequests.Complete(rejected, 12));
        var failed = false;
        var errorRequest = ManaWebGpuInitializationRequests.Register(handle => { failed = handle == 0; return true; });
        Check("request failure is delivered once without a native handle", ManaWebGpuInitializationRequests.Complete(errorRequest, 0) && failed);
    }
}
