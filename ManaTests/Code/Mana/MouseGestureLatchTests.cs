using Mana;
using ManaUI;

namespace ManaTests;

/// <summary>
/// The latch behind a held-drag camera gesture surviving the cursor sweeping across the UI mid-drag.
/// <para>
/// A CameraComponent subscribes to the window's resize event as it is built, so it cannot exist in a
/// headless suite -- this is checked directly against the latch instead, which is the actual logic and
/// needed no window to begin with.
/// </para>
/// </summary>
public static class MouseGestureLatchChecks
{
    public static void Run(ManaFontManager fonts, Action<string, bool, string> check)
    {
        void Check(string what, bool ok, string detail = "") => check(what, ok, detail);

        // ---------------------------------------------------------------- starting
        var latch = new MouseGestureLatch();

        Check("not active with the button up", !latch.Update(buttonDown: false, uiCapturesMouse: false));

        Check("starts once the button goes down over clear UI",
            latch.Update(buttonDown: true, uiCapturesMouse: false));

        // ---------------------------------------------------------------- the case this exists for
        // Held through the cursor sweeping over a docked panel mid-drag. This is the whole point: without
        // the latch, this frame alone would report inactive.
        Check("stays active when the cursor crosses the UI while still held",
            latch.Update(buttonDown: true, uiCapturesMouse: true));

        Check("and the frame after, and the one after that",
            latch.Update(buttonDown: true, uiCapturesMouse: true)
                && latch.Update(buttonDown: true, uiCapturesMouse: false));

        // ---------------------------------------------------------------- ending
        Check("clears the instant the button releases",
            !latch.Update(buttonDown: false, uiCapturesMouse: false));

        // A release while still nominally over the UI must clear it just the same -- otherwise letting go
        // over a panel would leave the next unrelated press starting pre-latched.
        latch.Update(buttonDown: true, uiCapturesMouse: false);
        Check("a release over the UI still clears it",
            !latch.Update(buttonDown: false, uiCapturesMouse: true));

        // ---------------------------------------------------------------- refusing to start
        // A press that begins while the UI wants the mouse is a click on something, not a drag -- so it
        // must never latch on, even if the UI's answer changes a frame later while the button is still down.
        var refused = new MouseGestureLatch();

        Check("does not start while the UI wants the mouse",
            !refused.Update(buttonDown: true, uiCapturesMouse: true));

        Check("and does not retroactively start once the UI clears, still holding the same press",
            !refused.Update(buttonDown: true, uiCapturesMouse: false));

        // Only releasing and pressing again, now over clear UI, can start it.
        refused.Update(buttonDown: false, uiCapturesMouse: false);
        Check("a fresh press after releasing can start it",
            refused.Update(buttonDown: true, uiCapturesMouse: false));

        // ---------------------------------------------------------------- independence
        // Two latches (right-drag look, middle-drag pan) must not share state -- holding one and tapping
        // the other must not affect the one already in progress.
        var right = new MouseGestureLatch();
        var middle = new MouseGestureLatch();

        right.Update(buttonDown: true, uiCapturesMouse: false);
        right.Update(buttonDown: true, uiCapturesMouse: true);

        // Middle never held, evaluated every frame alongside right -- as CameraComponent does, calling
        // both every frame rather than short-circuiting.
        var middleActive = middle.Update(buttonDown: false, uiCapturesMouse: true);

        Check("an untouched latch stays inactive alongside an active one", !middleActive, "independent");
        Check("and the active one is unaffected by it",
            right.Update(buttonDown: true, uiCapturesMouse: true), "still latched");
    }
}
