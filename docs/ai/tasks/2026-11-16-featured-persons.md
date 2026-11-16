# Featured person catalogs and music spotlight

- Owner: Codex, branch `codex/featured-persons`, base `eb8fa48`.
- Database: `d1cfd5ffdf7fc9c949e3607ce304e8b735a2bf35`.
- SDK: `7c19b39cb3c4d12787b2afff47e30fcf981f08e7`.
- Owned paths: Persons endpoint, operations/contracts, JSON metadata and local SQL/SDK-facing diagnostics.

`GET /Persons/FeaturedPersons?TypeCode=music` requires the normal API authentication.
The type is `music`, `movies` or `series`. Up to 12 editorial entries are returned
in catalog order. The music response includes up to three distinct active tracks
per person, rolling 30-day unique listeners and daily plays for seven UTC days.
The profile, cover and 3D model are Media IDs. Optional media stay optional. The
read is no-tracking and uses bounded queries with SQL-side aggregate work. Session-
specific state continues to use the normal Person follow operation for updates.

To provision local samples after migrating the developer database, from this
repository run:

```powershell
dotnet run --project Tools/SeedStoryTestData -- --seed-featured-persons --apply --model-directory '<Windows repository>/Assets/Discovery/Models'
```

This local-only fixture command fills empty catalogs, with no replacement of
populated catalogs. It uses the licensed Moein reconstruction from the Windows
repository, adds real credited music entries where available, and separates the
movie and series samples. It records no fictitious plays, credits, likes or
followers. The sample file URIs are for the developer machine and remain outside
the source repo. Shared deployments point Media rows at the normal accessible media storage.

Verified SQL migration round-trip and types/order, deduplicated credits, pagination,
follow permission/idempotency, counted listeners/plays and all three authenticated
HTTP routes. The local HTTP integration check also verifies 401 for anonymous reads.
