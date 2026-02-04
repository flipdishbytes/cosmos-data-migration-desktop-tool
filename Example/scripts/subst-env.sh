#!/usr/bin/env bash
# Reads stdin, replaces __VAR_NAME__ with value of env var VAR_NAME, writes to stdout.
# Used so connection strings and .last-sync-utc can be injected without sed escaping issues.
set -e
python3 -c "
import os, sys
s = sys.stdin.read()
for k in ['STORAGE_CONNECTION_STRING', 'COSMOS_CONNECTION_STRING', 'LAST_SYNC_UTC']:
    s = s.replace('__' + k + '__', os.environ.get(k, ''))
sys.stdout.write(s)
"
