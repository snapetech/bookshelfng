# Optional diagnostics module

BookshelfNG does not include diagnostics reporting in its standard builds. This
separate assembly is loaded only when it is installed and
`BOOKSHELF_DIAGNOSTICS_ENABLED=true` is set.

The module reports OpenTelemetry metrics to an operator-configured HTTPS OTLP
metrics endpoint. It aggregates only these event counts every 15 minutes:

- application starts
- books grabbed
- books imported
- downloads that failed

It sends the BookshelfNG service name and version with those counts. It does
not send book or author metadata, filenames, paths, search terms, log messages,
exception details, credentials, IP addresses in the payload, or a persistent
installation identifier. As with any network request, the configured collector
can observe the source IP address. No request is made unless the module is
installed, enabled, and configured with both an HTTPS endpoint and bearer
token. Redirects are disabled and TLS certificate validation uses the system
trust store. Counters are cumulative for the current process and held in
memory only; they are not written to disk and reset when BookshelfNG restarts.

## Build and install

Build the optional assembly separately from the standard BookshelfNG build:

```sh
dotnet build src/Bookshelf.Diagnostics/Bookshelf.Diagnostics.csproj -c Release
```

Copy the resulting assembly to the app data directory's `plugins` folder. In
the standard Linux container:

```sh
mkdir -p /config/plugins
cp _temp/bin/Release/Bookshelf.Diagnostics/net6.0/Bookshelf.Diagnostics.dll /config/plugins/
```

Set:

```yaml
environment:
  BOOKSHELF_DIAGNOSTICS_ENABLED: "true"
  BOOKSHELF_DIAGNOSTICS_OTLP_ENDPOINT: "https://collector.example/v1/metrics"
  BOOKSHELF_DIAGNOSTICS_AUTH_TOKEN: "${BOOKSHELF_DIAGNOSTICS_AUTH_TOKEN}"
```

Restart BookshelfNG to load or unload the module. Leave the enable flag unset,
or remove the assembly, to disable it. If enabled without the assembly,
BookshelfNG logs a warning and sends nothing. If the assembly is present but
endpoint or token configuration is missing or invalid, the module remains
inactive and sends nothing.

The module executes inside the BookshelfNG process and therefore runs with the
same operating-system privileges. Install only a module build you trust. The
standard BookshelfNG image does not contain this assembly or its reporting
code.
