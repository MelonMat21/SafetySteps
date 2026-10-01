# SafetySteps

A campus emergency-response app for Android, built with .NET MAUI and Firebase.
Students can report incidents (with a photo and live location), call an admin
over VoIP, and look up first-aid and disaster-preparedness guides. Admins
receive push alerts, triage reports on a live map, and answer a call queue.

## Features

**Students**
- Emergency report: incident type, location (school building/floor or court), photo, priority
- Live GPS tracking of an active report
- One-tap voice call to an available admin (urgent or normal)
- Report history with live "responded" status
- 20+ hazard and first-aid information guides

**Admins**
- High-priority push notifications for new reports and urgent calls
- Report dashboard with map view (Leaflet / OpenStreetMap), mark-as-responded, delete
- Call queue with caller profile, accept/reject
- Reset a student's password

## Tech stack

| Area | Technology |
|---|---|
| App | .NET 10 MAUI (Android) |
| Auth | Firebase Authentication (REST) |
| Database | Firebase Realtime Database (REST) |
| Push | Firebase Cloud Messaging (HTTP v1) |
| Voice calls | Agora RTC + token server |
| Maps | Leaflet + OpenStreetMap in a WebView |

## Project structure

```
SafetySteps/
├── App.xaml / AppShell.xaml   App entry point and Shell navigation
├── MauiProgram.cs             Dependency injection setup
├── Core/                      App-wide constants and session state
├── Models/                    Plain data models
├── Services/                  Calls, signaling, images, notifications
│   └── Firebase/              Auth, database, FCM, session, secrets loader
├── ViewModels/                Call view models
├── Views/
│   ├── Auth/                  Login, sign-up
│   ├── Student/               Home, alert, history, profile
│   ├── Admin/                 Dashboard, reports, profile, password reset
│   ├── Calls/                 Outgoing, incoming, call queue
│   ├── Info/                  Hazard guides
│   └── Shared/                Settings, about, tutorial
├── Platforms/Android/         Activity, FCM service, manifest
├── Resources/                 Images, fonts, styles, splash, raw assets
├── functions/                 Firebase Cloud Functions (Node)
├── database.rules.json        Realtime Database security rules
└── firebase.json              Firebase CLI config
```

## Getting started

### Prerequisites
- [.NET 10 SDK](https://dotnet.microsoft.com/download) with the MAUI workload: `dotnet workload install maui-android`
- Android SDK (installed with Visual Studio 2022+ or Android Studio)
- An Android device or emulator (API 23+)

### 1. Add the local secrets file
The service-account key used for push notifications and admin password resets
is **not** committed. Create it from the template:

```bash
cp .env.example .env.local
```

Fill in both values from a Firebase service-account key
(Firebase console → Project settings → Service accounts → Generate new private key).
`.env.local` is gitignored and is embedded into the app at build time.

### 2. Build and run
Open `SAFETY_STEPS.sln` in Visual Studio and run on an Android target, or:

```bash
dotnet build -t:Run -f net10.0-android
```

### 3. Database rules
Security rules live in `database.rules.json`. Deploy them with the Firebase CLI:

```bash
firebase deploy --only database
```

or paste the file into Firebase console → Realtime Database → Rules.

## Admin accounts

Admins cannot be created from the app. To make someone an admin:
1. They sign up in the app as a Student.
2. In Firebase console → Realtime Database → `users/<their uid>`, set `role` to `admin`.

## Security notes
- Never commit `.env.local` or any `*firebase-adminsdk*.json` file.
- The service-account key is embedded in the APK, so treat any build you
  distribute as able to expose it. Moving FCM sending and password resets into
  Cloud Functions would remove the key from the app entirely.
