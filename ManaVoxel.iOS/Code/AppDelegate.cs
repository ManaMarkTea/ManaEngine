namespace ManaVoxel.iOS;

[Register("AppDelegate")]
public sealed class AppDelegate : UIApplicationDelegate
{
    public static void Main(string[] args)
    {
        // The engine finds Content/ and Assets/ beside its working folder, as when run from the repo: the app carries
        // them in Game/, laid out the same. Its bundle is read-only, so the log goes to Documents (the Files app).
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Trace($"Unhandled: {e.ExceptionObject}");
        TaskScheduler.UnobservedTaskException += (_, e) => Trace($"Unobserved task exception: {e.Exception}");
        Trace("Main");
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        Environment.SetEnvironmentVariable("MANA_LOG_ROOT", documents);
        Environment.CurrentDirectory = Path.Combine(NSBundle.MainBundle.BundlePath, "Game");
        IosEmoji.Install();
        Trace($"Game folder {Environment.CurrentDirectory}, exists {Directory.Exists(Environment.CurrentDirectory)}");
        UIApplication.Main(args, null, typeof(AppDelegate));
        Trace("UIApplication.Main returned");
    }

    /// <summary>A startup step on the console, which `deploy-ios-from-windows.ps1 -Console` shows.</summary>
    public static void Trace(string message) => Console.WriteLine($"[ManaVoxel.iOS] {message}");

    public override void WillTerminate(UIApplication application) => Trace("WillTerminate");

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
        AppDelegate.Trace($"Scene connecting: {scene.GetType().Name}");
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
