---
category: fixed
audience: users, operators
area: integrations
action: Set BOOKSHELF_ALLOW_API_KEY_QUERY=false to require header credentials.
breaking: false
---
Docker images now preserve the executable permissions needed by non-root users. SeerrNG and other Readarr-compatible clients can again authenticate with `?apikey=`; operators can disable query credentials when they require header-based authentication.
