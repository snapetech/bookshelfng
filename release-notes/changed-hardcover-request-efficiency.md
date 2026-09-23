---
category: changed
audience: users, operators
area: hardcover
action: none
breaking: false
---
Hardcover text searches now fetch returned works in one batched query. ISBN/ASIN searches and edition lookups fetch parent works in the same operation. Requests are paced at one per second, and BookshelfNG stops sending requests during server cooldowns or depleted quotas.
