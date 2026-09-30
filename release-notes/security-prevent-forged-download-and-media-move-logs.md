---
category: security
audience: operators
area: logging
action: none
breaking: false
---
BookshelfNG now strips line breaks from download identifiers and media paths before writing them to diagnostic logs, preventing forged log entries through user-controlled values.
