# Project notes

## Database performance rule

The database is limited in the number of requests between the server and the DB.
Never read a whole table from the database on every page open or filter change.
Use `IMemoryCache` for reads of large tables, and invalidate the cache when the
underlying data changes (uploads/deletes) — see `InvalidateScorecardHistoricalCache`
in `Services/UploadService.cs` and the `*CacheKey` constants in the services it
clears. This applies to every new view, including the job headcount projection
display (`job_headcount_projections`).
