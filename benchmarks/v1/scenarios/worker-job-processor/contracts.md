# Contract: worker job processor

## Invocation

The application is started as `dotnet <app>.dll` with no command-line arguments. All configuration comes
from environment variables (see `instructions.md`). The current working directory is not read from and not
written to.

## Job files

- The worker enumerates every file whose name ends with `.json` in `JOBS_DIRECTORY` (non-recursive). Files
  with any other extension and sub-directories are ignored.
- Jobs are processed in ascending ordinal order of file name (`StringComparer.Ordinal`).
- Each job file contains a single JSON object with the following properties:

  | Property | Type | Required | Default | Meaning |
  | --- | --- | --- | --- | --- |
  | `id` | string | yes | – | Stable identifier used in logs and results. |
  | `payload` | string | yes | – | Arbitrary payload text (may be empty or contain non-ASCII characters). |
  | `failuresBeforeSuccess` | integer | no | `0` | Number of leading attempts that fail before the job succeeds. |
  | `fatal` | boolean | no | `false` | When `true` every attempt fails permanently. |

- The worker treats JSON property names as case-insensitive. Additional unknown properties are ignored.

## Processing

- Each job is processed with at most `MAX_ATTEMPTS` attempts.
- Attempt `N` (1-indexed) for a job is a **failure** if `fatal` is `true` or if `N <= failuresBeforeSuccess`.
  Otherwise the attempt is a **success** and produces the result value `payload.ToUpperInvariant()`.
- Between two attempts of the same job the worker waits `RETRY_DELAY_MS` milliseconds. There is no delay
  before the first attempt of a job and no delay after the last attempt.
- A job that never succeeds within `MAX_ATTEMPTS` attempts is recorded as failed and does **not** stop the
  worker; the next job is processed normally.
- The `attempts` counter for a job equals every attempt that was made, including the successful one.

## Output

- After every job completes (either successfully or exhausted) the worker writes (or rewrites)
  `OUTPUT_DIRECTORY/results.json`. `OUTPUT_DIRECTORY` is created if it does not already exist.
- `results.json` contains a JSON array whose elements appear in the order in which jobs were processed
  (i.e. ordinal order of file names, up to and including any jobs completed before shutdown). Each element
  is a JSON object with exactly these properties:

  | Property | Type | Value |
  | --- | --- | --- |
  | `id` | string | The job's `id`. |
  | `status` | string | `"succeeded"` or `"failed"`. |
  | `attempts` | integer | Number of attempts that were made for the job (>= 1). |
  | `result` | string or `null` | The uppercased payload on success; `null` on failure. |
  | `error` | string or `null` | `null` on success; a non-empty descriptive message on failure. |

- Property names are camelCase. The document may be pretty-printed or minified.

## Logging

The worker writes exactly one line to standard output per job outcome:

- `Job <id> succeeded after <n> attempt(s)` when the job finished successfully.
- `Job <id> failed after <n> attempt(s)` when the job was recorded as failed.

`<n>` is the same integer as the job's `attempts` value. The literal text `attempt(s)` is used regardless
of whether `<n>` is `1`.

## Completion

When every job in `JOBS_DIRECTORY` has been processed:

- `results.json` has been written with an entry for every job.
- The worker logs a single line `Processed <n> job(s)` to standard output, where `<n>` is the total number
  of jobs.
- The host stops and the process exits with code `0`.

An empty `JOBS_DIRECTORY` (a directory that exists but contains no `*.json` file) is valid: the worker
writes `results.json` containing `[]`, logs `Processed 0 job(s)` and exits with code `0`.

## Graceful shutdown

When the host receives a shutdown signal (SIGINT / SIGTERM / `Ctrl-C`) while processing:

- The worker stops starting new jobs and stops any in-progress retry delay for the current job.
- It writes `results.json` with an entry for every job that has already completed (any partially-processed
  job is not included).
- It logs a single line `Shutdown requested` to standard output.
- The host exits with code `0` within 10 seconds of the signal.

## Configuration errors

All of these are reported to standard error and cause the process to exit with code `2`:

- `JOBS_DIRECTORY` is not set: `Error: JOBS_DIRECTORY is not set.`
- `JOBS_DIRECTORY` is set but the directory does not exist:
  `Error: jobs directory '<path>' was not found.` where `<path>` is the value of the variable.
- `OUTPUT_DIRECTORY` is not set: `Error: OUTPUT_DIRECTORY is not set.`
- `MAX_ATTEMPTS` is set but is not a positive integer:
  `Error: MAX_ATTEMPTS must be a positive integer.`
- `RETRY_DELAY_MS` is set but is not a non-negative integer:
  `Error: RETRY_DELAY_MS must be a non-negative integer.`

## Edge cases that are graded

- A job with `failuresBeforeSuccess` equal to `MAX_ATTEMPTS - 1` succeeds on the last allowed attempt.
- A job with `failuresBeforeSuccess` equal to or greater than `MAX_ATTEMPTS` is recorded as `failed` with
  `attempts` equal to `MAX_ATTEMPTS`.
- Raising `MAX_ATTEMPTS` can turn a previously-failing job into a `succeeded` job.
- Payloads that are the empty string are valid and produce an empty `result` on success.
- Payloads containing spaces, punctuation and non-ASCII characters are preserved exactly (only case is
  changed).
- `fatal: true` overrides `failuresBeforeSuccess`: the job always fails.
- A malformed job file (invalid JSON, or missing `id`/`payload`) is recorded as failed with
  `attempts` equal to `1`, `result` equal to `null` and a non-empty `error` message. The `id` used in the
  result and log line is the file name without its `.json` extension.
