namespace ManaVoxel.iOS;

[Register("AppDelegate")]
public sealed class AppDelegate : UIApplicationDelegate
{
    public static void Main(string[] args)
    {
        // The engine finds Content/ and Assets/ beside its working folder, as when run from the repo: the app carries
        // them in Game/, laid out the same. Its bundle is read-only, so the log goes to Documents (the Files app).
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        Environment.SetEnvironmentVariable("MANA_LOG_ROOT", documents);
        Environment.CurrentDirectory = Path.Combine(NSBundle.MainBundle.BundlePath, "Game");
        UIApplication.Main(args, null, typeof(AppDelegate));
    }

    public override bool FinishedLaunching(UIApplication application, NSDictionary? launchOptions) => true;
}

/// <summary>The app's one window (iOS 26 makes every window belong to a scene; Info.plist names this class).</summary>
[Register("SceneDelegate")]
public sealed class SceneDelegate : UIResponder, IUIWindowSceneDelegate
{
    [Export("window")]
    public UIWindow? Window { get; set; }

    [Export("scene:willConnectToSession:options:")]
    public void WillConnect(UIScene scene, UISceneSession session, UISceneConnectionOptions connectionOptions)
    {
        if (scene is not UIWindowScene windowScene)
        {
            return;
        }
        Window = new UIWindow(windowScene)
        {
            RootViewController = new GameViewController(),
            OverrideUserInterfaceStyle = UIUserInterfaceStyle.Dark,
        };
        Window.MakeKeyAndVisible();
    }
}
