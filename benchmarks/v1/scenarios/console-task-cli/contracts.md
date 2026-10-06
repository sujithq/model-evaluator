# Contract: console task-management CLI

## Invocation

The application is started as `dotnet <app>.dll <command> [arguments]` with the current working directory
containing (or about to contain) the task store.

## Storage

- Tasks are stored in a file named `tasks.json` in the current working directory.
- The file contains a JSON array of task objects, each with exactly these properties:
  `id` (integer), `title` (string), `completed` (boolean), `createdAt` (ISO-8601 UTC timestamp string).
- The file is created on the first write. A missing file means "no tasks".
- Ids start at 1 and are assigned as `max(existing id) + 1`, so deleting a task in the middle of the list
  never reuses its id.
- Every command that changes data writes the file before the process exits.

## Commands

### `add <title>`

- The title is the remainder of the command line, joined with single spaces.
- Success: prints `Added task <id>: <title>` to stdout and exits with code `0`.
- Empty or whitespace-only title: prints `Error: title is required.` to stderr and exits with code `2`.
- Title longer than 200 characters: prints `Error: title must be 200 characters or fewer.` to stderr and
  exits with code `2`. A title of exactly 200 characters is valid.

### `list [--status all|pending|completed]`

- Without `--status`, or with `--status all`, every task is listed.
- `--status pending` lists only tasks with `completed = false`; `--status completed` only completed tasks.
- Tasks are listed in ascending id order, one per line, formatted as `<id> [ ] <title>` for pending tasks and
  `<id> [x] <title>` for completed tasks.
- When no task matches, prints `No tasks found.` to stdout. Exit code `0` in all of these cases.
- An unsupported `--status` value prints `Error: unknown status '<value>'.` to stderr and exits with code `2`.

### `complete <id>`

- Marks the task as completed, prints `Completed task <id>` to stdout and exits with code `0`.
- Completing an already completed task is allowed and produces the same output and exit code.
- Unknown id: prints `Error: task <id> not found.` to stderr and exits with code `3`.
- Non-integer or missing id: prints `Error: id must be an integer.` to stderr and exits with code `2`.

### `delete <id>`

- Removes the task, prints `Deleted task <id>` to stdout and exits with code `0`.
- Unknown id: prints `Error: task <id> not found.` to stderr and exits with code `3`.
- Non-integer or missing id: prints `Error: id must be an integer.` to stderr and exits with code `2`.

### Other invocations

- No arguments: prints usage text containing the word `Usage` to stdout and exits with code `1`.
- Unknown command `<name>`: prints `Error: unknown command '<name>'.` to stderr and exits with code `1`.

## Edge cases that are graded

- State persists across separate process executions.
- Deleting a task that is not the highest id does not reuse that id for the next task.
- A corrupt or unreadable `tasks.json` makes any command print `Error: task store is corrupt.` to stderr and
  exit with code `4`.
- Titles containing spaces, punctuation and non-ASCII characters are preserved exactly.
