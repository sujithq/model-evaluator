# Contract: Blazor task-management app

## Hosting

- The application is started as `dotnet <app>.dll` and binds to the URL passed via `ASPNETCORE_URLS`.
- The task pages must use **static server-side rendering** (Blazor SSR). WebAssembly and interactive
  JavaScript are not permitted for the journeys below; every request must succeed with `curl` and a plain
  HTML form post.
- The mutation endpoints below are graded with `HttpClient.AllowAutoRedirect = false`. They must therefore
  be mapped with `.DisableAntiforgery()` so an antiforgery token is **not** required on the form posts.

## Storage

- Tasks are stored in a JSON file. The path is read from the environment variable `TASKS_FILE`; when it is
  unset, the file `tasks.json` in the application content root is used.
- The file contains a JSON array of task objects, each with exactly these camelCase properties:
  `id` (integer), `title` (string), `completed` (boolean), `createdAt` (ISO-8601 UTC timestamp string).
- The file is created on the first write. A missing file means "no tasks".
- Ids start at 1 and are assigned as `max(existing id) + 1`, so deleting a task in the middle of the list
  never reuses its id.
- Every request that changes data writes the file before it returns.
- State must survive an application restart (dispose the host, start a new one against the same
  `TASKS_FILE`; the tasks come back).

## Endpoints

### `GET /health`

- Returns `200` with body `ok` (`text/plain`). Used as the readiness probe.

### `GET /tasks`

- Returns `200 text/html`. The page contains the following elements:
  - An add form: `<form method="post" action="/tasks/add">` with an input named `title`.
  - Zero or more task rows. Each row is an element whose `id` attribute is `task-<id>` and contains:
    - the exact task title as text,
    - the literal status marker `[x]` for completed tasks and `[ ]` for pending tasks,
    - a complete form posting to `/tasks/{id}/complete`,
    - an edit form posting to `/tasks/{id}/edit` with an input named `title`,
    - a delete form posting to `/tasks/{id}/delete`.
- When there are no tasks the page contains the exact text `No tasks found.`.

### `GET /tasks?status=pending|completed|all`

- Filters the rendered list. The default (query missing or empty) is `all`.
- `pending` renders only tasks with `completed = false`; `completed` renders only completed tasks.
- Any other value returns `400` and the page contains the exact text `Unknown status filter.`.

### `POST /tasks/add` (form-encoded `title`)

- On success: `302` redirect to `/tasks`. The task is persisted to the JSON file before the response
  returns.
- Blank or whitespace-only title: `400` with a page containing the exact text `Title is required.`.
- Title longer than 200 characters: `400` with a page containing the exact text
  `Title must be 200 characters or fewer.`. A title of exactly 200 characters is valid.

### `POST /tasks/{id}/complete`

- On success: `302` redirect to `/tasks`. Completing an already completed task is allowed and produces the
  same response.
- Unknown id: `404`.

### `POST /tasks/{id}/edit` (form-encoded `title`)

- On success: `302` redirect to `/tasks`.
- Unknown id: `404`.
- Same title validation as `POST /tasks/add`.

### `POST /tasks/{id}/delete`

- On success: `302` redirect to `/tasks`. The removal is persisted before the response returns.
- Unknown id: `404`.

## Edge cases that are graded

- Ids are never reused: adding a task after deleting the highest id still increments beyond it.
- Titles containing spaces, punctuation and non-ASCII characters are preserved exactly.
- The empty-state text, the status markers and the validation messages appear byte-for-byte as specified.
- State is preserved across process restarts pointed at the same `TASKS_FILE`.
