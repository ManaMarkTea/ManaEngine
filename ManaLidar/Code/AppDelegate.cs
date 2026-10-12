namespace ManaLidar;

[Register("AppDelegate")]
public sealed class AppDelegate : UIApplicationDelegate
{
    public override UIWindow? Window { get; set; }

    public static void Main(string[] args) => UIApplication.Main(args, null, typeof(AppDelegate));

    public override bool FinishedLaunching(UIApplication application, NSDictionary? launchOptions)
    {
        Window = new UIWindow(UIScreen.MainScreen.Bounds)
        {
            RootViewController = new UINavigationController(new ScanViewController()),
            OverrideUserInterfaceStyle = UIUserInterfaceStyle.Dark,
        };
        Window.MakeKeyAndVisible();
        return true;
    }
}
