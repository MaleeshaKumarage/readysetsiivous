#!/usr/bin/env bash
set -euo pipefail
# PostgreSQL sources non-executable init scripts. Return to its entrypoint.
bash /database/setup.sh
