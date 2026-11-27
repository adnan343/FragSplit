\getenv migration_password SHOP_MIGRATION_PASSWORD
\getenv runtime_password SHOP_RUNTIME_PASSWORD

SELECT 'CREATE ROLE vialmuse_migrator'
WHERE NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'vialmuse_migrator') \gexec
SELECT 'CREATE ROLE vialmuse_api'
WHERE NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'vialmuse_api') \gexec

SELECT format('ALTER ROLE vialmuse_migrator LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS NOINHERIT PASSWORD %L', :'migration_password') \gexec
SELECT format('ALTER ROLE vialmuse_api LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS NOINHERIT PASSWORD %L', :'runtime_password') \gexec

ALTER DATABASE fragsplit OWNER TO vialmuse_migrator;
REVOKE ALL ON DATABASE fragsplit FROM PUBLIC;
GRANT CONNECT ON DATABASE fragsplit TO vialmuse_api;
ALTER SCHEMA public OWNER TO vialmuse_migrator;
REVOKE ALL ON SCHEMA public FROM PUBLIC;

-- Upgrade the existing application objects from the original bootstrap owner.
SELECT format('ALTER TABLE %I.%I OWNER TO vialmuse_migrator', schemaname, tablename)
FROM pg_tables WHERE schemaname = 'public' AND tableowner = current_user \gexec
SELECT format('ALTER SEQUENCE %I.%I OWNER TO vialmuse_migrator', schemaname, sequencename)
FROM pg_sequences WHERE schemaname = 'public' AND sequenceowner = current_user \gexec
SELECT format('ALTER FUNCTION %s OWNER TO vialmuse_migrator', p.oid::regprocedure)
FROM pg_proc p
JOIN pg_namespace n ON n.oid = p.pronamespace
WHERE n.nspname = 'public' AND p.prokind = 'f' AND pg_get_userbyid(p.proowner) = current_user \gexec
