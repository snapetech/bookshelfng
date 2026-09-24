---
category: security
audience: operators
area: logging
action: none
breaking: false
---
BookshelfNG now strips control and Unicode line-separator characters from request paths and origin headers before logging, preventing untrusted request data from creating misleading entries in plain-text logs.
