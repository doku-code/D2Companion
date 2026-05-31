# Data Folder

`sample-catalog.json` is the committed test fixture. It is sanitized and should be enough to test the current UI without MPQ files or real MuleLogger data.

Local runtime files stay private and are ignored by Git:

- `catalog.json`
- `*.sqlite`
- `*.sqlite3`
- `*.db`
- `captures/`
- `raw/`

When the Styx ingestion/database work starts, write captured data into ignored local files first, then promote only anonymized fixtures into `sample-catalog.json` when needed for repeatable UI tests.
