using Android;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Views;
using AndroidX.Core.App;
using AndroidX.Core.Content;

namespace SAFETY_STEPS
{
    [Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
    public class MainActivity : MauiAppCompatActivity
    {
        private const int NotificationPermissionRequestCode = 1001;

        protected override void OnCreate(Bundle? savedInstanceState)
        {
            base.OnCreate(savedInstanceState);
            Window?.AddFlags(WindowManagerFlags.Fullscreen);
            if (!OperatingSystem.IsAndroidVersionAtLeast(35))
                Window?.SetStatusBarColor(Android.Graphics.Color.Transparent);

            CreateNotificationChannels();
            RequestNotificationPermission();
        }

        // Tapping the incoming-call notification while the app is running
        // delivers its intent here (LaunchMode.SingleTop).
        protected override void OnNewIntent(Intent? intent)
        {
            base.OnNewIntent(intent);

            if (intent?.GetStringExtra("navigate_to") == "incoming_call" &&
                UserSession.IsLoggedIn &&
                Microsoft.Maui.Storage.Preferences.Get("user_role", "") == "admin")
            {
                AppShell.NavigateAdminToIncomingCallPage();
            }
        }

        // Android 13+ shows no notifications at all unless the user grants this.
        private void RequestNotificationPermission()
        {
            if (!OperatingSystem.IsAndroidVersionAtLeast(33)) return;

            if (ContextCompat.CheckSelfPermission(this, Manifest.Permission.PostNotifications) != Permission.Granted)
            {
                ActivityCompat.RequestPermissions(this,
                    new[] { Manifest.Permission.PostNotifications },
                    NotificationPermissionRequestCode);
            }
        }

        // Channels must exist before a background push arrives, otherwise Android
        // files the notification under a low-importance fallback channel.
        private void CreateNotificationChannels()
        {
            if (!OperatingSystem.IsAndroidVersionAtLeast(26)) return;

            var manager = (NotificationManager?)GetSystemService(NotificationService);
            if (manager == null) return;

            var highPriority = new NotificationChannel(
                "high_priority_channel",
                "High Priority Alerts",
                NotificationImportance.High)
            {
                Description = "Used for emergency high priority alerts",
                LockscreenVisibility = NotificationVisibility.Public,
            };
            highPriority.EnableVibration(true);
            highPriority.SetVibrationPattern(new long[] { 0, 500, 200, 500, 200, 500 });
            highPriority.EnableLights(true);
            highPriority.LightColor = Android.Graphics.Color.Red;
            manager.CreateNotificationChannel(highPriority);

            var calls = new NotificationChannel(
                "call_channel",
                "Incoming Calls",
                NotificationImportance.High)
            {
                Description = "Incoming emergency calls",
                LockscreenVisibility = NotificationVisibility.Public,
            };
            calls.EnableVibration(true);
            manager.CreateNotificationChannel(calls);

            var standard = new NotificationChannel(
                "default_channel",
                "Alerts",
                NotificationImportance.Default)
            {
                Description = "Reports and call queue updates",
            };
            standard.EnableVibration(true);
            manager.CreateNotificationChannel(standard);
        }
    }
}
