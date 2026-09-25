# Reporting BookshelfNG issues

BookshelfNG writes its logs under the application data directory. In the
LinuxServer container, the debug log is normally
`/config/logs/readarr.debug.txt`; standalone installs use the `logs` directory
inside the configured data directory. The **System → Logs** page can also be
used to view and download logs.

## UI does not load

Include the following in a report:

- The full URL opened, including any reverse-proxy path prefix, and the HTTP
  status shown by the browser.
- The `X-Bookshelf-Request-Id` response header if the request receives a
  response. It matches the request ID in BookshelfNG's logs.
- The `Web UI entry point` and `HTTP server` startup lines from
  `readarr.debug.txt` or the container log.
- Any nearby `HTTP response` or `UI resource` lines, including the requested
  path and status.
- The image version and tag, revision, flavor, OS, and process architecture
  from the startup `Build/runtime` line.

The container build now fails if the packaged UI is missing its HTML entry
point, JavaScript, or CSS assets. At runtime BookshelfNG also logs whether
`UI/index.html` exists beside the application binaries.

## SeerrNG reports a command 404

Include the SeerrNG warning and the matching BookshelfNG `Api` and `Command`
log lines. The request path includes the command ID, for example
`/api/v1/command/3126`, and its log line includes the same request ID returned
in the `X-Bookshelf-Request-Id` header. Include the timestamp and timezone so
the two applications' logs can be matched.

Command records are retained in BookshelfNG's database for one day after they
finish. A 404 means the requested command ID is no longer present or the request
reached a different BookshelfNG instance or base path; the surrounding request
and command logs help distinguish those cases.

## Which log file to send

Start with `readarr.debug.txt`, which includes API response statuses, failed UI
requests, request IDs, command creation, and missing-command details. Send a
small time window around the failure rather than the entire log. If the debug
log does not show the request, temporarily change `<LogLevel>debug</LogLevel>`
to `<LogLevel>trace</LogLevel>` in `config.xml`, reproduce once, and include
the matching lines from `readarr.trace.txt`; restore `debug` afterward.

For container reports, include the exact image tag and architecture. The app
logs its build version, source revision, and image flavor; the registry digest
is assigned by the container registry and can be read from Docker with:

```sh
docker inspect --format='tag={{.Config.Image}} image-id={{.Image}}' <container-name>
docker image inspect --format='repo-digests={{json .RepoDigests}}' <image-id>
```

Do not include API keys, passwords, cookies, or authorization headers. The HTTP
request logger does not record request bodies or authentication headers.
Query-string credentials and book-search terms are redacted from HTTP request
logs. Trace-level request lines include the remote IP address and User-Agent;
review excerpts before sharing them.
