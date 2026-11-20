# Contract: minimal Web API task manager

## Hosting and storage

- The application is started as `dotnet <app>.dll`. It must honour the `ASPNETCORE_URLS` environment
  variable and listen on the URL provided there.
- Tasks are stored in a SQLite database file. The file path is taken from the environment variable
  `TASKS_DB_PATH`. When the variable is unset or empty the application uses `tasks.db` in the content
  root (`IHostEnvironment.ContentRootPath`).
- If the database file does not exist it is created on startup. If the required schema does not exist it is
  created on startup. Nothing else is written outside the content root.
- Ids are assigned by the database, monotonically increasing per creation. Deleting a task never causes a
  future insert to reuse its id.
- The server sets `createdAt` when a task is created and never changes it after that. `PUT` does not modify
  `createdAt`.
- Data must survive an application restart against the same database file: stopping the process and starting
  a new one with the same `TASKS_DB_PATH` must expose the same tasks.

## Resource shape

Every task is serialised as a JSON object with exactly these four properties, using camelCase names:

```json
{
  "id": 1,
  "title": "Buy milk",
  "completed": false,
  "createdAt": "2026-01-01T12:34:56.789Z"
}
```

- `id` is a positive integer.
- `title` is a non-empty string trimmed of no characters (leading and trailing whitespace are preserved as
  submitted, provided the trimmed length is at least one character; see validation below).
- `completed` is a boolean.
- `createdAt` is an ISO-8601 timestamp in UTC. Both `Z` and an explicit `+00:00` offset are acceptable, as
  long as the value round-trips through `DateTimeOffset.Parse` and represents UTC.

## Endpoints

All response bodies other than plain-text `GET /health` and the empty `204` responses are JSON with
`Content-Type: application/json`. Validation errors use
[RFC 7807](https://datatracker.ietf.org/doc/html/rfc7807) `ProblemDetails` with
`Content-Type: application/problem+json`.

### `GET /health`

- Always returns `200 OK` with body `ok` and `Content-Type: text/plain`.
- Used as the readiness probe by the evaluator.

### `GET /tasks`

- Returns `200 OK` with a JSON array of tasks in **ascending id order**.
- Query parameters:
  - `status`: one of `all` (default), `pending`, `completed`. `pending` returns only tasks with
    `completed = false`; `completed` returns only tasks with `completed = true`; `all` returns every task.
    Any other value returns `400 Bad Request` with a `ProblemDetails` body describing the invalid
    `status` value.
  - `search`: an optional case-insensitive substring match on the title. If provided, only tasks whose
    title contains the value (case-insensitive) are returned. An empty `search` value is treated as if the
    parameter was not provided.
- When both parameters are supplied, both must match (logical AND).
- When no task matches, returns `200 OK` with an empty JSON array `[]`.

### `GET /tasks/{id}`

- `200 OK` with the task JSON when a task with that id exists.
- `404 Not Found` (with an empty body or a `ProblemDetails` body) when it does not exist.

### `POST /tasks`

- Request body: `{ "title": "..." }` as `application/json`.
- On success, returns `201 Created` with:
  - the created task in the response body,
  - a `Location` header exactly equal to `/tasks/{id}` where `{id}` is the new task's id.
- The `completed` flag of a newly created task is always `false`.
- Validation errors return `400 Bad Request` with a `ProblemDetails` body:
  - Missing `title`, `null` title or a title that is empty or whitespace-only.
  - Title longer than 200 characters after the JSON string has been decoded.
- A title of exactly 200 characters is valid.

### `PUT /tasks/{id}`

- Request body: `{ "title": "...", "completed": true|false }` as `application/json`.
- On success, returns `200 OK` with the updated task in the response body. Both `title` and `completed`
  are replaced with the values supplied. `id` and `createdAt` are unchanged.
- `404 Not Found` when no task has the supplied id.
- Validation errors return `400 Bad Request` with a `ProblemDetails` body under the same title rules as
  `POST /tasks` (missing/blank title, or longer than 200 characters). A title of exactly 200 characters
  is valid. A missing or non-boolean `completed` field is a validation error.

### `DELETE /tasks/{id}`

- `204 No Content` when the task existed and was removed.
- `404 Not Found` when no task has the supplied id.

### Unknown routes and unsupported methods

- Any request that does not match one of the endpoints above returns `404 Not Found`.

## Edge cases that are graded

- Ids are not reused after a delete: deleting the task with the highest id and then creating a new task
  still returns a strictly higher id.
- Non-ASCII characters in titles are preserved exactly through create, read and update.
- Concurrent requests to the same instance must not corrupt the database (a single-writer implementation
  using the default SQLite journaling is sufficient).
- The database file survives a process restart: stopping the process and starting a new one against the
  same `TASKS_DB_PATH` returns the same tasks.
- Filtering by `search` is case-insensitive but does not use SQL `LIKE` wildcards from the client: the raw
  value is treated as a literal substring.
