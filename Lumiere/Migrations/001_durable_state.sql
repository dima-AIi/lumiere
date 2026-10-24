-- Additive migration. No existing records are replaced or deleted.
SELECT pg_advisory_xact_lock(438101291);
CREATE TABLE IF NOT EXISTS lumiere_documents (
    key text PRIMARY KEY,
    data jsonb NOT NULL
);
CREATE TABLE IF NOT EXISTS lumiere_media (
    name text PRIMARY KEY,
    body bytea NOT NULL CHECK (octet_length(body) <= 3145728)
);
CREATE TABLE IF NOT EXISTS lumiere_keys (
    id text PRIMARY KEY,
    xml text NOT NULL
);
CREATE TABLE IF NOT EXISTS lumiere_cache (
    id text PRIMARY KEY,
    value bytea NOT NULL,
    expires_at timestamptz NOT NULL,
    sliding_ms bigint,
    absolute_at timestamptz
);
CREATE INDEX IF NOT EXISTS lumiere_cache_expiry ON lumiere_cache(expires_at);
