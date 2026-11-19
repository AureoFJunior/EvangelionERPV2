# Sourced by every script that applies EF's idempotent migration script.
#
# EF 10 writes each migration as one batch: its DDL and data steps, the __EFMigrationsHistory
# insert, then COMMIT. A runtime error such as a duplicate key ends only the statement that raised
# it, so the rest of the batch still runs and commits: the migration is recorded as applied with
# part of it missing. sqlcmd's -b does not prevent that, because it exits only after the failing
# batch has finished. Reproduced on SQL Server 2022 on 2026-10-30 with a Msg 2627 in an EF-shaped
# batch: without this line the history row and the half-applied column both stayed.
#
# XACT_ABORT ON turns any error into a batch abort and a rollback of the open transaction, history
# row included. It is a session setting, so one line at the top covers every batch after it.
prepend_xact_abort() {
  local script="$1"
  local tmp="${script}.xact"
  {
    printf 'SET XACT_ABORT ON;\nGO\n'
    # dotnet-ef writes the file with a UTF-8 BOM. Left in place it lands mid-file and sqlcmd fails
    # the next batch with Msg 102. od rather than a sed escape so this also runs on macOS.
    if [[ "$(head -c 3 "$script" | od -An -tx1 | tr -d ' \n')" == "efbbbf" ]]; then
      tail -c +4 "$script"
    else
      cat "$script"
    fi
  } > "$tmp"
  mv "$tmp" "$script"
}
