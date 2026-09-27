# Component B Development Data and Mobile Session Storage

## Development fixtures

`ComponentBDataSeeder` runs only when the API environment is `Development` and
`Database:MigrateOnStartup` is enabled. It inserts one resolved, verified help
request with a three-step status history and one safe travel advisory outside
Sri Lanka. Both records are marked as demo data. The associated demo citizen is
inactive and receives a random password hash, so the account cannot be used to
sign in. The fixed fixture IDs make repeated startup seeding idempotent.

Production startup does not insert these records.

## Flutter session storage

`AuthService` stores the serialized session in `flutter_secure_storage`, backed
by the platform secure store. When restoring a session, it checks secure storage
first. If an older version left a session in `SharedPreferences`, the service
moves that value to secure storage and removes the plaintext preference. Sign
out removes both keys. Tests cover secure writes, restore, expiry cleanup, and
the one-time migration.
