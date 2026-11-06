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

    /// <summary>Firebase Web API key for the Rust Companion app (public client identifier, not a secret).</summary>
    public const string ApiKey = "AIzaSyB5y2y-Tzqb4-I4Qnlsh_9naYv_TD8pCvY";

    /// <summary>Firebase project identifier for the Rust Companion app.</summary>
    public const string ProjectId = "rust-companion-app";

    /// <summary>GCM sender ID (Firebase project number) for the Rust Companion app.</summary>
    public const string GcmSenderId = "976529667804";

    /// <summary>Google Mobile Services application ID for the Rust Companion Android app.</summary>
    public const string GmsAppId = "1:976529667804:android:d6f1ddeb4403b338fea619";

    /// <summary>Android package name of the Rust Companion app.</summary>
    public const string AndroidPackageName = "com.facepunch.rust.companion";

    /// <summary>SHA-1 certificate fingerprint of the Rust Companion APK.</summary>
    public const string AndroidPackageCert = "E28D05345FB78A7A1A63D70F4A302DBF426CA5AD";

    // --- Expo ---

    /// <summary>Expo project ID for the Rust Companion app used when requesting an Expo push token.</summary>
    public const string ExpoProjectId = "49451aca-a822-41e6-ad59-955718d0ff9c";

    // --- Endpoints ---

    /// <summary>GCM check-in endpoint (protobuf over HTTPS).</summary>
    public const string CheckinUrl = "https://android.clients.google.com/checkin";

    /// <summary>FCM token registration endpoint.</summary>
    public const string FcmRegisterUrl = "https://android.clients.google.com/c2dm/register3";

    /// <summary>Firebase Installations Service (FIS) endpoint for the Rust Companion project.</summary>
    public const string FirebaseInstallationsUrl =
        "https://firebaseinstallations.googleapis.com/v1/projects/" + ProjectId + "/installations";

    /// <summary>Expo push token exchange endpoint.</summary>
    public const string ExpoPushTokenUrl = "https://exp.host/--/api/v2/push/getExpoPushToken";

    /// <summary>Rust Companion device registration endpoint.</summary>
    public const string CompanionRegisterUrl = "https://companion-rust.facepunch.com/api/push/register";

    /// <summary>Facepunch Steam login page. After Steam sign-in it ends in Facepunch's Rust+ app redirect
    /// (<see cref="RustPlusAppReturnUrl"/>), which answers with a <c>rust-plus://</c> <c>Location</c>.</summary>
    public const string SteamLoginUrl = "https://companion-rust.facepunch.com/login";

    /// <summary>The <c>returnUrl</c> that makes Facepunch's <c>/app</c> endpoint redirect to the Rust+ app scheme.</summary>
    public const string RustPlusAppReturnUrl = "/app?returnUrl=rust-plus://rust-auth-session";

    /// <summary>Prefix of the <c>rust-plus://</c> redirect that carries <c>steamId</c> and <c>token</c>.</summary>
    public const string RustPlusAuthRedirectPrefix = "rust-plus://rust-auth-session";

    /// <summary>URL prefix of Facepunch's <c>/app</c> endpoint, which issues the Rust+ app redirect.</summary>
    public const string CompanionAppUrlPrefix = "https://companion-rust.facepunch.com/app";

    /// <summary>URL prefix of Facepunch's <c>/login-failed</c> page.</summary>
    public const string CompanionLoginFailedUrlPrefix = "https://companion-rust.facepunch.com/login-failed";

    /// <summary>Chrome identity used for the GCM check-in (mirrors push-receiver).</summary>
    public const string ChromeVersion = "63.0.3234.0";
}
