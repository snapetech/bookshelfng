---
category: security
audience: operators
area: integrations
action: Use the canonical service URL when authenticated requests would redirect to another origin.
breaking: false
---
BookshelfNG no longer forwards authentication, cookies, or request bodies across redirect origins. Set an integration to its final service URL directly if it depends on an authenticated redirect to another server.
