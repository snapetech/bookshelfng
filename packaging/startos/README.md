# StartOS submission packet

The compiled StartOS wrapper is published at
https://github.com/snapetech/bookshelfng-startos.

- Package ID: `bookshelfng`
- Image: `docker.io/snapetech/bookshelfng:hardcover-v0.4.21.37`
- Web UI: TCP `8787`
- Persistent data: `/config`, `/books`, `/downloads`

The wrapper passes the current Start9 SDK typecheck and `ncc` build. Start9's
marketplace submission is a human-gated review: send the wrapper repository URL
and this source repository to `submissions@start9labs.com`, including the
license and runtime-port details above.
