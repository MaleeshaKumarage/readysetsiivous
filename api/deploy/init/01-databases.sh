#!/bin/bash
# Runs once on first postgres boot (docker-entrypoint-initdb.d).
# Creates the keycloak database + role; password comes from the postgres
# container's KC_DB_PASSWORD env (same value as compose KC_DB_PASSWORD).
set -e

psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" <<-EOSQL
	CREATE USER keycloak WITH PASSWORD '${KC_DB_PASSWORD}';
	CREATE DATABASE keycloak OWNER keycloak;
EOSQL
