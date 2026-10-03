# Security policy

Use a supported BookshelfNG release and keep the host, container image,
reverse proxy, and operating system updated.

Report suspected vulnerabilities through GitHub's private vulnerability
reporting for this repository. If that feature is unavailable, contact a
repository maintainer privately through GitHub before public disclosure.
Include the affected version, deployment type, steps to reproduce, and impact.
Do not include live API keys, passwords, cookies, or unredacted logs in a
public issue.

BookshelfNG API keys grant access to the API. Use the restricted
`BOOKSHELF_SEERRNG_API_KEY` for SeerrNG when possible, keep credentials in a
secret manager, and rotate exposed values. Configure trusted proxy addresses
before relying on forwarded client IP or HTTPS headers.
