-- IntelliMaint Pro - TimescaleDB Initialization Script
-- This script runs automatically when the container starts for the first time

-- Enable TimescaleDB extension
CREATE EXTENSION IF NOT EXISTS timescaledb;

-- Enable btree_gist for exclusion constraints (optional)
CREATE EXTENSION IF NOT EXISTS btree_gist;

-- Set timezone
SET timezone = 'UTC';

-- Log initialization
DO $$
BEGIN
    RAISE NOTICE 'TimescaleDB extensions initialized successfully';
END $$;
