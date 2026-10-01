using Android.App;
using Android.Content;
using AndroidX.Core.App;
using Firebase.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui;
using SAFETY_STEPS.FireBase;
using SAFETY_STEPS.Models;
using SAFETY_STEPS.ViewModels;

namespace SAFETY_STEPS.Platforms.Android
{
    [Service(Exported = true)]
    [IntentFilter(new[] { "com.google.firebase.MESSAGING_EVENT" })]
    public class MyFirebaseMessagingService : FirebaseMessagingService
    {
        private const string CallChannelId = "call_channel";
        private const string AlertChannelId = "high_priority_channel";
        private const string DefaultChannelId = "default_channel";

        public override void OnNewToken(string token)
        {
            base.OnNewToken(token);
            Microsoft.Maui.Storage.Preferences.Set("fcm_token", token);
            System.Console.WriteLine($"[FCM] New token: {token}");
            SaveTokenToRtdbAsync(token);   // ← renamed from SaveTokenToFirestoreAsync
        }

        // ── Save FCM token to Realtime DB (was Firestore) ─────────────────
        private static void SaveTokenToRtdbAsync(string token)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    var uid = UserSession.Uid;
                    if (string.IsNullOrEmpty(uid))
                    {
                        System.Console.WriteLine("[FCM] UID not available yet — token will be saved on login.");
                        return;
                    }

                    // FirestoreService is now actually RealtimeDatabaseService (same class name)
                    var db = IPlatformApplication.Current?.Services.GetService<FirestoreService>()
                        ?? new FirestoreService(global::SAFETY_STEPS.App.AuthService);

                    await db.PatchFieldsAsync("users", uid,
                        new Dictionary<string, object> { { "fcmToken", token } });

                    System.Console.WriteLine($"[FCM] Token saved to RTDB for uid='{uid}'");
                }
                catch (Exception ex)
                {
                    System.Console.WriteLine($"[FCM] Failed to save token: {ex.Message}");
                }
            });
        }

        public override void OnMessageReceived(RemoteMessage message)
        {
            base.OnMessageReceived(message);

            message.Data.TryGetValue("type", out var type);
            System.Console.WriteLine($"[FCM] OnMessageReceived — type='{type}'");

            if (type == "INCOMING_CALL")
            {
                HandleIncomingCall(message);
                return;
            }

            var title = message.GetNotification()?.Title;
            var body = message.GetNotification()?.Body;

            if (string.IsNullOrEmpty(title)) message.Data.TryGetValue("title", out title);
            if (string.IsNullOrEmpty(body)) message.Data.TryGetValue("body", out body);

            var channelId = type == "HIGH_PRIORITY_ALERT" ? AlertChannelId : DefaultChannelId;
            ShowNotification(title ?? "Alert", body ?? "You have a new alert.", channelId);
        }

        private void HandleIncomingCall(RemoteMessage message)
        {
            message.Data.TryGetValue("channelName", out var channelName);
            message.Data.TryGetValue("callerName", out var callerName);
            message.Data.TryGetValue("isUrgent", out var isUrgentRaw);
            var isUrgent = string.Equals(isUrgentRaw, "true", StringComparison.OrdinalIgnoreCase);

            System.Console.WriteLine($"[FCM] Incoming call — channel='{channelName}' caller='{callerName}'");

            if (string.IsNullOrEmpty(channelName))
            {
                System.Console.WriteLine("[FCM] ⚠️ channelName missing from FCM payload — cannot accept call.");
                return;
            }

            var call = new CallData
            {
                CallId = channelName,
                ChannelName = channelName,
                CallerName = callerName ?? "Unknown",
                IsUrgent = isUrgent,
                Status = "calling"
            };

            if (isUrgent)
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    try
                    {
                        var vm = IPlatformApplication.Current?.Services
                                     .GetService<IncomingCallViewModel>();
                        if (vm != null)
                        {
                            vm.NotifyIncomingCall(call);
                            System.Console.WriteLine("[FCM] ✅ IncomingCallViewModel seeded from urgent FCM payload.");
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Console.WriteLine($"[FCM] VM seed error: {ex.Message}");
                    }
                });
            }

            if (isUrgent)
                ShowCallNotification(callerName ?? "Unknown", channelName);
            else
                ShowNotification("New call in queue", $"{callerName ?? "Someone"} is waiting.", DefaultChannelId);
        }

        private void ShowCallNotification(string callerName, string channelName)
        {
            var notificationManager = (NotificationManager?)GetSystemService(NotificationService);
            EnsureChannel(notificationManager, CallChannelId, "Incoming Calls", NotificationImportance.High);

            var intent = new Intent(this, typeof(MainActivity));
            intent.AddFlags(ActivityFlags.ClearTop | ActivityFlags.SingleTop);
            intent.PutExtra("navigate_to", "incoming_call");
            intent.PutExtra("channel_name", channelName);
            intent.PutExtra("caller_name", callerName);

            var pendingIntent = PendingIntent.GetActivity(
                this, 0, intent,
                PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);

            var notification = new NotificationCompat.Builder(this, CallChannelId)
                .SetContentTitle("📞 Incoming Call")
                .SetContentText($"{callerName} is calling you")
                .SetSmallIcon(GetAppIcon())
                .SetAutoCancel(true)
                .SetPriority(NotificationCompat.PriorityMax)
                .SetCategory(NotificationCompat.CategoryCall)
                .SetDefaults(NotificationCompat.DefaultAll)
                .SetFullScreenIntent(pendingIntent, true)
                .SetContentIntent(pendingIntent)
                .Build();

            notificationManager?.Notify(1001, notification);
        }

        private void ShowNotification(string title, string body, string channelId)
        {
            var notificationManager = (NotificationManager?)GetSystemService(NotificationService);
            EnsureChannel(notificationManager, channelId, "Alerts", NotificationImportance.Default);

            var intent = new Intent(this, typeof(MainActivity));
            intent.AddFlags(ActivityFlags.ClearTop);
            var pendingIntent = PendingIntent.GetActivity(
                this, 0, intent,
                PendingIntentFlags.OneShot | PendingIntentFlags.Immutable);

            var notification = new NotificationCompat.Builder(this, channelId)
                .SetContentTitle(title)
                .SetContentText(body)
                .SetSmallIcon(GetAppIcon())
                .SetAutoCancel(true)
                .SetPriority(NotificationCompat.PriorityHigh)
                .SetDefaults(NotificationCompat.DefaultAll)
                .SetContentIntent(pendingIntent)
                .Build();

            // Unique id per notification so a new alert never replaces an unread one.
            notificationManager?.Notify(System.Environment.TickCount, notification);
        }

        private static void EnsureChannel(NotificationManager? mgr, string id, string name,
            NotificationImportance importance)
        {
            if (global::Android.OS.Build.VERSION.SdkInt < global::Android.OS.BuildVersionCodes.O) return;
            if (mgr?.GetNotificationChannel(id) != null) return;

            var channel = new NotificationChannel(id, new Java.Lang.String(name), importance);
            channel.EnableVibration(true);
            mgr?.CreateNotificationChannel(channel);
        }

        private int GetAppIcon()
        {
            try
            {
                var field = typeof(Resource.Drawable).GetField("appicon",
                    System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public);
                if (field != null) return (int)field.GetValue(null)!;
            }
            catch { }
            return global::Android.Resource.Drawable.IcDialogInfo;
        }
    }
}