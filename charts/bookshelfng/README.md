# BookshelfNG Helm chart

This chart runs BookshelfNG as one StatefulSet replica and persists `/config`
with a PVC. BookshelfNG's SQLite database must not be shared by multiple active
replicas. The default `hardcover` image tag selects the Hardcover profile;
`softcover` selects the Goodreads-compatible profile.

Install a specific release from GHCR:

```sh
helm upgrade --install bookshelfng \
  oci://ghcr.io/snapetech/bookshelfng/bookshelfng \
  --version 0.4.20-19
```

For a Softcover image, set `--set image.tag=softcover`. Use `values.yaml` to
configure storage, ingress, service type, resources, and Kubernetes environment
values. Reference Kubernetes Secrets through `extraEnv[].valueFrom` for
credentials; do not put provider tokens in Helm values committed to source.

See the [full installation guide](https://github.com/snapetech/bookshelfng/blob/main/docs/standalone-install.md).
