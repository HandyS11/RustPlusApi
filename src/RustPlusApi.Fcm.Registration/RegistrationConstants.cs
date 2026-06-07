using System.Diagnostics.CodeAnalysis;

namespace RustPlusApi.Fcm.Registration;

/// <summary>
/// Firebase / Expo / Rust Companion constants and endpoints for the registration flow.
/// </summary>
/// <remarks>
/// These values are read from liamcottle/rustplus.js (the <c>fcm-register</c> CLI) and the
/// <c>@liamcottle/push-receiver</c> source. They <b>drift</b> when Google/Facepunch change
/// their apps — if registration starts failing, re-check them against those upstream sources
/// rather than trusting this file. This is the most upstream-fragile code in the repo.
/// </remarks>
[SuppressMessage("Minor Code Smell", "S1075:URIs should not be hardcoded",
    Justification = "These are the fixed Google/Expo/Facepunch API endpoints for the flow.")]
[SuppressMessage("Security", "S6418:Hard-coded secrets should not be used",
    Justification = "Firebase Web API keys are public client identifiers embedded in the app, not secrets.")]
public static class RegistrationConstants
{
    // --- Rust+ Firebase project (rustplus.js) ---

    public const string ApiKey = "AIzaSyB5y2y-Tzqb4-I4Qnlsh_9naYv_TD8pCvY";
    public const string ProjectId = "rust-companion-app";
    public const string GcmSenderId = "976529667804";
    public const string GmsAppId = "1:976529667804:android:d6f1ddeb4403b338fea619";
    public const string AndroidPackageName = "com.facepunch.rust.companion";
    public const string AndroidPackageCert = "E28D05345FB78A7A1A63D70F4A302DBF426CA5AD";

    // --- Expo ---

    public const string ExpoProjectId = "49451aca-a822-41e6-ad59-955718d0ff9c";

    // --- Endpoints ---

    public const string CheckinUrl = "https://android.clients.google.com/checkin";
    public const string FcmRegisterUrl = "https://android.clients.google.com/c2dm/register3";
    public const string FirebaseInstallationsUrl = "https://firebaseinstallations.googleapis.com/v1/projects/" + ProjectId + "/installations";
    public const string ExpoPushTokenUrl = "https://exp.host/--/api/v2/push/getExpoPushToken";
    public const string CompanionRegisterUrl = "https://companion-rust.facepunch.com/api/push/register";
    public const string SteamLoginUrl = "https://companion-rust.facepunch.com/login";

    /// <summary>Chrome identity used for the GCM check-in (mirrors push-receiver).</summary>
    public const string ChromeVersion = "63.0.3234.0";
}
