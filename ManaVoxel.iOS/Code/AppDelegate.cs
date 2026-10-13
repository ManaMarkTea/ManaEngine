namespace ManaVoxel.iOS;

[Register("AppDelegate")]
public sealed class AppDelegate : UIApplicationDelegate
{
    public override UIWindow? Window { get; set; }

    public static void Main(string[] args)
    {
        // The engine finds Content/ and Assets/ beside its working folder, as when run from the repo: the app carries
        // them in Game/, laid out the same. Its bundle is read-only, so the log goes to Documents (the Files app).
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        Environment.SetEnvironmentVariable("MANA_LOG_ROOT", documents);
        Environment.CurrentDirectory = Path.Combine(NSBundle.MainBundle.BundlePath, "Game");
        UIApplication.Main(args, null, typeof(AppDelegate));
    }

    public override bool FinishedLaunching(UIApplication application, NSDictionary? launchOptions)
    {
        Window = new UIWindow(UIScreen.MainScreen.Bounds)
        {
            RootViewController = new GameViewController(),
            OverrideUserInterfaceStyle = UIUserInterfaceStyle.Dark,
        };
        Window.MakeKeyAndVisible();
        return true;
    }
}
