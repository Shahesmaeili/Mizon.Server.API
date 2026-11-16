# Person profile fixtures

- Owner: Codex, branch `codex/person-profile-studio`, base `27d03eb`.
- Owned paths: the local seeding operation, explicit CLI and fixture assets.

Populate ten synthetic, independent profiles in the local development database.

```powershell
dotnet run --project Tools/SeedStoryTestData -- --seed-person-profiles --apply --assets-directory (Resolve-Path 'Mizon.Server.API/App_Data/PersonProfileFixtures')
```

The prerequisite is the existing FeaturedPersons music catalog and local lookup
data. The operation inserts only its own stable profile IDs and appends them
to the current music catalog. All updates are atomic. A complete fixture set
is left in place on a repeat run. Includes unique avatar/cover/GLB media, two
generated PCM WAV tracks each, track covers, genres, waveforms, synthetic likes,
plays and follow histories. The linked test Client records have no credential
or username. Media paths point to the local developer checkout.

Assets and prompt set are recorded in
[the fixture art notes](../../../Mizon.Server.API/App_Data/PersonProfileFixtures/SOURCES.md).
The existing Persons, media and music routes read these as normal records.
