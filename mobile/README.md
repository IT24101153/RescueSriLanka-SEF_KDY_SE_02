# Rescue Sri Lanka mobile app

This is the Flutter mobile application for Rescue Sri Lanka.

## Run in Android Studio

1. Install Android Studio and the Flutter plugin. The Dart plugin is installed automatically with the Flutter plugin.
2. In Android Studio, choose **Open** and select this folder:
	`RescueSriLanka-SEF_KDY_SE_02/mobile`
3. Open **SDK Manager** and install:
	- Android SDK Platform 36
	- Android SDK Build-Tools
	- Android SDK Command-line Tools (latest)
	- Android SDK Platform-Tools
4. Open **Device Manager**, create or start an Android emulator, or connect a phone with USB debugging enabled.
5. In the Android Studio terminal, run:

	```powershell
	flutter pub get
	flutter doctor --android-licenses
	flutter devices
	```

6. Select the emulator or phone in the device selector and press the green **Run** button. The entrypoint is `lib/main.dart`.

## Run from a terminal

From the `mobile` folder:

```powershell
flutter run
```

To create a debug APK:

```powershell
flutter build apk --debug
```

The APK is generated at `build/app/outputs/flutter-apk/app-debug.apk`.

## Troubleshooting

If Android Studio cannot find Flutter, set the Flutter SDK path to `C:\src\flutter` in **Settings > Languages & Frameworks > Flutter**. If `flutter doctor` reports missing command-line tools or licenses, install the components above and run `flutter doctor --android-licenses` again.

## Resource requests and donations

The mobile app has two tabs:

- **Request help** sends food, water, medical, rescue, or shelter needs to `POST /api/resources/help-requests`.
- **Donate** sends a resource offer to `POST /api/resources/donations` for the resource manager to review.

The emulator uses `http://10.0.2.2:5093` to reach the local API. Start the API with:

```powershell
cd ..\backend\RescueSriLanka.Api
dotnet run --launch-profile http
```

For Supabase, set the API `DefaultConnection` to the Supabase PostgreSQL connection string and run `dotnet ef database update`, or execute `backend/supabase_resource_tables.sql` in the Supabase SQL Editor. The app must continue using the API; never place a Supabase service-role key in Flutter.
