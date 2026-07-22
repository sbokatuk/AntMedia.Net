using Foundation;
using UIKit;

namespace AntMedia.Net.Apple.DeviceTests;

public static class Application
{
    private static void Main(string[] args) => UIApplication.Main(args, null, typeof(AppDelegate));
}

/// <summary>
/// Runs the smoke tests once the app has launched, prints the verdict to stdout and exits.
/// .github/scripts/run-ios-device-tests.sh launches the app with `simctl launch --console-pty`,
/// and run-mac-device-tests.sh runs the Mac Catalyst build straight from the shell; both stream
/// stdout and return when the process ends — so exiting here is what ends the step, and the
/// marker strings are part of the contract with those scripts.
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

    private static async void RunSmokeTests()
    {
        var failures = 0;
        var checks = SmokeTests.All.Count;

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

        // Only when a server was supplied. Most runs have none, and a skipped live check must not
        // read as a passed one — hence SKIP on its own line rather than silence.
        var serverUrl = Environment.GetEnvironmentVariable("ANTMEDIA_TEST_SERVER");

        if (string.IsNullOrWhiteSpace(serverUrl))
        {
            Console.WriteLine("SKIP live publish (no ANTMEDIA_TEST_SERVER)");
        }
        else
        {
            checks++;
            try
            {
                Console.WriteLine($"    {await LiveStreamTest.RunAsync(serverUrl)}");
                Console.WriteLine("PASS live publish");
            }
            catch (Exception exception)
            {
                failures++;
                Console.WriteLine($"FAIL live publish: {exception}");
            }
        }

        Console.WriteLine(failures == 0
            ? $"{DoneMarker} PASS ({checks} checks)"
            : $"{DoneMarker} FAIL ({failures} of {checks} checks failed)");

        Console.Out.Flush();
        Environment.Exit(failures == 0 ? 0 : 1);
    }
}
