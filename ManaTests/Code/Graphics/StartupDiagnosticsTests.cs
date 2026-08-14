using ManaCore.Diagnostics;
using ManaUI;

namespace ManaTests;

/// <summary>
/// The startup-timeline logging added to chase down why the loading screen was not appearing until nearly
/// the end of a load. Nothing about the timeline itself can be checked headlessly -- there is no window,
/// no device, and so no gap to measure -- so this only guards the one thing that can go wrong on its own
/// and matters everywhere: logging must never be able to throw, since half these call sites are inside a
/// GPU present path and a startup diagnostic is not worth a crashed frame.
/// </summary>
public static class StartupDiagnosticsChecks
{
    public static void Run(ManaFontManager fonts, Action<string, bool, string> check)
    {
        void Check(string what, bool ok, string detail = "") => check(what, ok, detail);

        Exception? thrown = null;
        try
        {
            ManaLog.WriteDiagnosticElapsed("Headless smoke check A");
            ManaLog.WriteDiagnosticElapsed("Headless smoke check B");
        }
        catch (Exception ex)
        {
            thrown = ex;
        }

        Check("elapsed logging never throws", thrown == null, thrown?.Message ?? "clean");

        // A degenerate label is still a caller mistake, not a reason to take down whatever was mid-frame
        // when it happened.
        Exception? thrownOnEmpty = null;
        try
        {
            ManaLog.WriteDiagnosticElapsed(string.Empty);
        }
        catch (Exception ex)
        {
            thrownOnEmpty = ex;
        }

        Check("an empty label still does not throw", thrownOnEmpty == null, thrownOnEmpty?.Message ?? "clean");
    }
}
