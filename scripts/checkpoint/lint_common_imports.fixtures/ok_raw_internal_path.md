# Fixture: ok_raw_internal_path

This file references an internal tool path — NOT a subcommand path.
The rawpath guard must NOT fire on it.

The scope check is done by scripts/checkpoint/verify-phase-scope.stash internally.
