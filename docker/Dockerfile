FROM mcr.microsoft.com/dotnet/aspnet:10.0.12-alpine3.24 AS dotnet-runtime

FROM ghcr.io/linuxserver/baseimage-alpine:3.24

COPY --from=dotnet-runtime /usr/share/dotnet /usr/share/dotnet

ARG TARGETPLATFORM

ENV DOTNET_ROOT=/usr/share/dotnet \
    PATH="/usr/share/dotnet:${PATH}" \
    XDG_CONFIG_HOME="/config/xdg" \
    COMPlus_EnableDiagnostics=0 \
    TMPDIR=/run/readarr-temp

RUN apk add -U --upgrade --no-cache \
    ffmpeg \
    icu-libs \
    sqlite-libs \
    xmlstarlet

RUN test -x /usr/share/dotnet/dotnet && \
    test -d /usr/share/dotnet/shared/Microsoft.NETCore.App/10.0.12 && \
    test -d /usr/share/dotnet/shared/Microsoft.AspNetCore.App/10.0.12

ARG GIT_BRANCH
ARG COMMIT_HASH
ARG BUILD_DATE
ARG IMAGE_VERSION=unknown
ARG IMAGE_FLAVOR=unknown
ARG METADATA_URL=https://api.bookinfo.pro
ARG HARDCOVER

LABEL maintainer="pennydreadful" \
    org.opencontainers.image.version=${IMAGE_VERSION} \
    org.opencontainers.image.revision=${COMMIT_HASH} \
    org.opencontainers.image.ref.name=${GIT_BRANCH} \
    com.snapetech.bookshelf.flavor=${IMAGE_FLAVOR} \
    com.snapetech.bookshelf.image-tag=${IMAGE_FLAVOR}-v${IMAGE_VERSION}

ENV METADATA_URL=${METADATA_URL}
ENV HARDCOVER=${HARDCOVER}
ENV BOOKSHELF_IMAGE_VERSION=${IMAGE_VERSION} \
    BOOKSHELF_IMAGE_REVISION=${COMMIT_HASH} \
    BOOKSHELF_IMAGE_REF=${GIT_BRANCH} \
    BOOKSHELF_IMAGE_FLAVOR=${IMAGE_FLAVOR} \
    BOOKSHELF_IMAGE_TAG=${IMAGE_FLAVOR}-v${IMAGE_VERSION} \
    BOOKSHELF_IMAGE_BUILD_DATE=${BUILD_DATE} \
    BOOKSHELF_CONTAINER=true

RUN mkdir -p /app/readarr

RUN --mount=type=bind,source=_output,target=_output \
    case "$TARGETPLATFORM" in \
    "linux/amd64") cp -r /_output/net10.0/linux-musl-x64 /app/readarr/bin ;; \
    "linux/arm64") cp -r /_output/net10.0/linux-musl-arm64 /app/readarr/bin ;; \
    "darwin/amd64") cp -r /_output/net10.0/osx-x64 /app/readarr/bin ;; \
    "darwin/arm64") cp -r /_output/net10.0/osx-arm64 /app/readarr/bin ;; \
    "windows/amd64") cp -r /_output/net10.0/win-x64 /app/readarr/bin ;; \
    *) echo "Unknown platform: $TARGETPLATFORM" && exit 1 ;; \
    esac; \
    cp -r /_output/UI /app/readarr/bin/UI

RUN test -s /app/readarr/bin/UI/index.html && \
    test -n "$(find /app/readarr/bin/UI -type f -name '*.js' -print -quit)" && \
    test -n "$(find /app/readarr/bin/UI -type f -name '*.css' -print -quit)"

RUN printf 'UpdateMethod=docker\nBranch=%s\nPackageVersion=%s\nPackageAuthor=pennydreadful\nReleaseVersion=%s\n' \
        "${GIT_BRANCH}" "${COMMIT_HASH}" "${IMAGE_VERSION}" > /app/readarr/package_info && \
    printf 'version: %s\nBuild-date: %s\n' "${COMMIT_HASH}" "${BUILD_DATE}" > /build_version

# copy local files
COPY docker/root/ /

# ports and volumes
EXPOSE 8787

VOLUME /config
