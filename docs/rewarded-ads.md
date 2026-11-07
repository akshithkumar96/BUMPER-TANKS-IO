# Rewarded daily spin setup

Rewarded video is optional and is disabled until a `LevelPlaySettings` asset exists at `Assets/Resources/LevelPlaySettings.asset` with the platform app key and rewarded ad unit ID filled in. Create it with **Assets → Create → Bumper Tanks → LevelPlay Settings**. Consent defaults are conservative (`gdprConsent` false and `ccpaDoNotSell` true); review the game's consent flow and jurisdiction requirements before changing them. The settings are applied before `LevelPlay.Init`.

Install package `com.unity.services.levelplay` 9.6.0 (already listed in `Packages/manifest.json`), then configure Android/iOS apps, mediated networks, and rewarded ad units in the LevelPlay dashboard. WebGL does not initialize the SDK; its ordinary server daily spin remains available.

Set the server secret as `Rewards__LevelPlayPrivateKey` in the deployment secret store. Never put it in Unity assets or the client. Configure the LevelPlay rewarded S2S callback to call:

`https://<your-api-host>/v1/rewards/ad/levelplay-callback`

Include the provider's user ID, reward units, event ID, timestamp, and signature fields, plus the `challengeId` custom parameter supplied by the SDK using `LevelPlay_Rewarded_Server_Params`. LevelPlay timestamps use UTC `YYYYMMDDHHMM` format. The signed string is exactly `timestamp + eventId + userId + rewards + privateKey` (MD5 hex). The app requests an authenticated one-time challenge before showing an ad. Only one challenge can be outstanding per player; this binds the callback's unsigned custom challenge ID to that player and prevents shifting a valid signed event to another pending challenge. Only the S2S callback completes it; client-side ad callbacks merely poll for the server result. Challenges and event-to-challenge bindings persist with profile data, and repeats of the same callback are acknowledged only for the original binding without another grant. Callback timestamps are checked against the challenge window and LevelPlay's retry age.

Before release, set the callback URL in LevelPlay and validate its exact custom-parameter macro names against the account's S2S callback settings. Keep the endpoint reachable over HTTPS. The app keys, ad unit IDs, signing key, and dashboard callback configuration are external deployment setup and are intentionally not committed.
