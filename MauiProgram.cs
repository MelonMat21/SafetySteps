using Microsoft.Extensions.Logging;
using SAFETY_STEPS.FireBase;
using SAFETY_STEPS.InfoPage;
using SAFETY_STEPS.Services;
using SAFETY_STEPS.ViewModels;
using CommunityToolkit.Maui; 
using CommunityToolkit.Maui.Core; 

namespace SAFETY_STEPS
{
    public static class MauiProgram
    {
        public static MauiApp CreateMauiApp()
        {
            var builder = MauiApp.CreateBuilder();
            builder
                .UseMauiApp<App>()
                .UseMauiCommunityToolkit()              // ← base toolkit first
                .UseMauiCommunityToolkitMediaElement(true)
                .ConfigureFonts(fonts =>
                {
                    fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                    fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");

                    fonts.AddFont("Poppins-Regular.ttf", "PoppinsRegular");
                    fonts.AddFont("Poppins-Bold.ttf", "PoppinsBold");
                    fonts.AddFont("Poppins-SemiBold.ttf", "PoppinsSemiBold");
                });

#if ANDROID
            builder.ConfigureMauiHandlers(h =>
                h.AddHandler<Microsoft.Maui.Controls.WebView,
                             SAFETY_STEPS.Platforms.Android.MauiWebViewHandler>());
#endif

            // ── Firebase services ────────────────────────────────────────────
            builder.Services.AddSingleton<FirebaseAuthService>();
            builder.Services.AddSingleton<FirestoreService>();

            // ── Admin resolution ─────────────────────────────────────────────
            builder.Services.AddSingleton<AdminService>();

            // ── Call services ────────────────────────────────────────────────
            builder.Services.AddSingleton<AgoraCallService>();
            builder.Services.AddSingleton<CallSignalingService>();
            builder.Services.AddSingleton<AgoraTokenService>();

            // ── Call ViewModels ───────────────────────────────────────────────
            builder.Services.AddTransient<CallViewModel>();
            builder.Services.AddSingleton<IncomingCallViewModel>();

            // ── Shell ─────────────────────────────────────────────────────────
            builder.Services.AddSingleton<AppShell>();

            // ── Login / Register ──────────────────────────────────────────────
            builder.Services.AddTransient<LoginPage>();
            builder.Services.AddTransient<SignupPage>();

            // ── Tab bar pages ─────────────────────────────────────────────────
            builder.Services.AddTransient<StudentProfile>();
            builder.Services.AddTransient<HomePage>();
            builder.Services.AddTransient<AlertPage>();
            builder.Services.AddTransient<HistoryPage>();
            builder.Services.AddTransient<InfosPage>();
            builder.Services.AddTransient<CallsPage>();
            builder.Services.AddTransient<HomePageAdmin>();
            builder.Services.AddTransient<AdminProfile>();
            builder.Services.AddTransient<ReportHistoryAdmin>();

            // ── Call pages ────────────────────────────────────────────────────
            builder.Services.AddTransient<CallPage>();
            builder.Services.AddSingleton<IncomingCallPage>();

            // ── Info sub-pages ────────────────────────────────────────────────
            builder.Services.AddTransient<SAFETY_STEPS.InfosPage>();
            builder.Services.AddTransient<SAFETY_STEPS.InfoPage.Earthquake>();

            // ── Shell routes ──────────────────────────────────────────────────
            Routing.RegisterRoute(nameof(SAFETY_STEPS.InfoPage), typeof(SAFETY_STEPS.InfosPage));
            Routing.RegisterRoute(nameof(SAFETY_STEPS.InfoPage.Earthquake), typeof(SAFETY_STEPS.InfoPage.Earthquake));

#if DEBUG
            builder.Logging.AddDebug();
#endif
            return builder.Build();
        }
    }
}