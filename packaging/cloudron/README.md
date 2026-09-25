# Cloudron community package

The Cloudron package uses the public Hardcover Docker Hub image and persists
`/config`, `/books`, and `/downloads`. Build it from this directory with the
Cloudron CLI, then publish the generated `CloudronVersions.json` URL as a
community app in a Cloudron dashboard. Cloudron does not require a central
catalog PR for community apps.

After the source PR is merged, the version metadata URL is:
https://raw.githubusercontent.com/snapetech/bookshelfng/main/packaging/cloudron/CloudronVersions.json
