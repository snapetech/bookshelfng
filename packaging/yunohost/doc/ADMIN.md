## Access and service

The YunoHost permission for this app defaults to administrators because the app manages download and library services. You can widen that permission in YunoHost if other people should use the interface. The BookshelfNG service listens only on its localhost port; access it through the installed YunoHost domain and path.

Check service status and logs in the YunoHost admin interface, or run:

```sh
sudo yunohost service status bookshelfng
sudo yunohost service log bookshelfng
sudo journalctl -u bookshelfng
```

BookshelfNG also writes rotating application logs under `/home/yunohost.app/<app>/logs/`.

## Library and download folders

The service runs as the YunoHost app account (`bookshelfng`, or the instance name for additional installations). Give that account read and write access to the existing book, audiobook, download, and completed-download folders it needs. Those folders are outside the app's own backup and must be backed up separately.

FFmpeg is optional. Install it with `sudo apt install ffmpeg` if you want BookshelfNG's chaptered audiobook merge feature, then enable that feature in BookshelfNG settings.

## Data, backup, and upgrades

YunoHost stores BookshelfNG's configuration and databases in the persistent app data directory, normally `/home/yunohost.app/<app>/`. App backups stop the service briefly for a consistent SQLite snapshot. They include configuration, databases, and metadata state; transient cache and logs are omitted. Your book and audio files, download client data, and any other external folders are not included.

The package pins a published BookshelfNG release archive and verifies its SHA-256 checksum. YunoHost's upstream release updater tracks future stable `main-v*` releases and proposes package metadata updates; install those updates through the usual YunoHost app upgrade flow.
