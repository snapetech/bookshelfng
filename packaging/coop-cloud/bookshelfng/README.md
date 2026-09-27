# Co-op Cloud recipe draft

This directory contains a review-ready Co-op Cloud recipe draft for
BookshelfNG. It uses the public Hardcover Docker Hub image, Traefik for the
web UI, and named volumes for configuration, books, and downloads.

The recipe is not yet in the canonical `coop-cloud/apps` collection. Submit a
wishlist issue first, then copy this directory into the recipe repository after
the maintainers accept the app. The usual deployment flow is:

```sh
abra app new bookshelfng
abra app config bookshelfng.example.com
abra app deploy bookshelfng.example.com
```

The external `proxy` network and a reachable Docker Swarm host are required.
