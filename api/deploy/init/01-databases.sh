#!/usr/bin/env bash
# First boot only. Existing volumes require the separately reviewed rollout.
set -euo pipefail
# PostgreSQL sources non-executable init scripts. Never replace its entrypoint.
bash /database/setup.sh
