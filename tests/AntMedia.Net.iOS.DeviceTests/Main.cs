using Foundation;
using UIKit;

namespace AntMedia.Net.IOS.DeviceTests;

public static class Application
{
    private static void Main(string[] args) => UIApplication.Main(args, null, typeof(AppDelegate));
}

/// <summary>
/// Runs the smoke tests once the app has launched, prints the verdict to stdout and exits.
/// .github/scripts/run-ios-device-tests.sh launches the app with `simctl launch --console-pty`,
/// which streams stdout and returns when the process ends — so exiting here is what ends the CI
/// step, and the marker strings are part of the contract with that script.
/// </summary>
[Register(nameof(AppDelegate))]
public class AppDelegate : UIApplicationDelegate
{
    private const string DoneMarker = "ANTMEDIA_E2E_DONE";

    public override UIWindow? Window { get; set; }

    public override bool FinishedLaunching(UIApplication application, NSDictionary launchOptions)
    {
        // Deferred to the next turn of the run loop rather than run inline: the frameworks are
        // loaded as part of launch, and exiting from inside FinishedLaunching can cut off stdout
        // before the pipe is drained.
        NSRunLoop.Main.BeginInvokeOnMainThread(RunSmokeTests);
        return true;
    }

    private static void RunSmokeTests()
    {
        var failures = 0;

        foreach (var (name, run) in SmokeTests.All)
        {
            try
            {
                run(detail => Console.WriteLine($"    {detail}"));
                Console.WriteLine($"PASS {name}");
            }
            catch (Exception exception)
            {
                failures++;
                // The whole exception: for a missing framework the stack is what identifies which
                // one failed to load.
                Console.WriteLine($"FAIL {name}: {exception}");
            }
        }

        Console.WriteLine(failures == 0
            ? $"{DoneMarker} PASS ({SmokeTests.All.Count} checks)"
            : $"{DoneMarker} FAIL ({failures} of {SmokeTests.All.Count} checks failed)");

        Console.Out.Flush();
        Environment.Exit(failures == 0 ? 0 : 1);
    }
}
