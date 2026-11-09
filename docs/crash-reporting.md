# Crash reporting

Crash reports are optional diagnostics. The Unity client subscribes to `Application.logMessageReceived` and only considers `LogType.Exception`; it checks the existing telemetry consent setting before hashing and again immediately before starting the request. The user's choice defaults to off. Reports are deduplicated per fingerprint for the current session.

The client derives a normalized signature from the exception type and stack frame method names, then sends only its lowercase SHA-256 digest, the application build version, Unity platform name, and the authenticated player ID. Exception messages, raw stack traces, and bearer tokens are never report fields. Requests use the existing bearer identity and the endpoint verifies that the body player ID matches that identity.

`POST /v1/crash-reports` validates field lengths and formats, has a dedicated limit of ten requests per minute, and coalesces repeats for the same player and fingerprint. Persistence keeps only the digest, build/platform metadata, timestamps, player ID, and occurrence count; entries expire after 30 days and are capped at 1,000 records. PostgreSQL mode shares this data across service instances; Development JSON mode keeps it in its local file.

Verification includes Edit Mode checks for consent/type gating and digest normalization, a server API smoke for authorization/validation/acceptance/idempotence, and a repository smoke for persistence, expiry, dedupe, and retention bounds. Unity's exception log callback supplies `LogType.Exception` but does not expose an independent handled/unhandled flag; the client therefore uses that Unity callback as the exception signal.
