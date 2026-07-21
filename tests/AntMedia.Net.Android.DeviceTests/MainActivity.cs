using Android.App;
using Android.OS;
using Android.Util;
using Android.Widget;

namespace AntMedia.Net.Android.DeviceTests;

/// <summary>
/// Runs the smoke tests on launch and reports the verdict to logcat under the AntMediaE2E tag.
/// .github/scripts/run-android-device-tests.sh polls for ANTMEDIA_E2E_DONE and turns it into an
/// exit code, so the exact marker strings are part of the contract with that script.
/// </summary>
// Name is pinned because .NET for Android otherwise generates a hashed Java class name
// (crc64....MainActivity), and the runner script launches this activity by name with
// `am start -n <package>/.MainActivity`.
[Activity(
    Name = "com.sbokatuk.antmedia.devicetests.MainActivity",
    Label = "AntMedia.Net device tests",
    MainLauncher = true,
    Exported = true)]
public class MainActivity : Activity
{
    private const string Tag = "AntMediaE2E";
    private const string DoneMarker = "ANTMEDIA_E2E_DONE";

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        var output = new TextView(this);
        SetContentView(output);

        var failures = 0;

        foreach (var (name, run) in SmokeTests.All)
        {
            try
            {
                run(this, detail => Log.Info(Tag, $"    {detail}"));
                Log.Info(Tag, $"PASS {name}");
            }
            catch (Exception exception)
            {
                failures++;
                // The whole exception, not just the message: an UnsatisfiedLinkError's stack is
                // what tells you which native library failed to load.
                Log.Error(Tag, $"FAIL {name}: {exception}");
            }
        }

        var verdict = failures == 0
            ? $"{DoneMarker} PASS ({SmokeTests.All.Count} checks)"
            : $"{DoneMarker} FAIL ({failures} of {SmokeTests.All.Count} checks failed)";

        Log.Info(Tag, verdict);
        output.Text = verdict;
    }
}
