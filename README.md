# PlexBackup

PlexBackup creates or retrieves backups for Plex, Silo, and Home Assistant,
then uploads them to a plain FTP server.

## Usage

Run every module present in the configuration:

```sh
dotnet PlexBackup.dll backup --config /etc/backup.json
```

Run only one module:

```sh
dotnet PlexBackup.dll backup --config /etc/backup.json --module silo
```

Select several modules by repeating the option:

```sh
dotnet PlexBackup.dll backup --config /etc/backup.json \
  --module plex \
  --module homeassistant
```

Available module names are `plex`, `silo`, and `homeassistant`. There is no
`enabled` setting. Without `--module`, every configured module runs. When the
option is present, only the explicitly selected modules run.

Uploaded files are named `<service>-DDMMYYYY.<extension>`. The FTP destination
directory must already exist.

## Configuration

Copy [`backup.example.json`](backup.example.json) to `/etc/backup.json` and
adjust it for your systems:

```json
{
  "ftp": {
    "server": "ftp://ftp.example.com/backups",
    "username": "backup-user",
    "passwordCredential": "ftp_password"
  },
  "modules": {
    "plex": {
      "sourceDirectory": "/var/lib/plex/Plex Media Server",
      "tempDirectory": "/var/tmp/backup",
      "excludeDirectories": ["Cache", "Codecs", "Logs"]
    },
    "silo": {
      "server": "http://127.0.0.1:8090",
      "apiKeyCredential": "silo_api_key",
      "tempDirectory": "/var/tmp/backup",
      "timeoutSeconds": 3600,
      "pollIntervalSeconds": 5
    },
    "homeassistant": {
      "server": "https://ha.example.com",
      "tokenCredential": "homeassistant_token",
      "tempDirectory": "/var/tmp/backup"
    }
  }
}
```

The service account running PlexBackup must:

- be able to read the Plex data directory;
- be able to write to each `tempDirectory`.

The Silo API key must belong to an administrator and allow the native backup
operations. Home Assistant requires a
long-lived access token belonging to an administrator, and its URL must use
HTTPS.

### Silo backups

The Silo module requires the native `/api/v2/admin/backups` API described in
[Silo's backup API contract](https://github.com/Silo-Server/silo-server/blob/main/docs/backups-api.md).
It checks backup capabilities, starts one manual backup, polls that backup's ID
until it finishes, and downloads all objects with `scope: full`. Their sizes and
SHA-256 checksums are checked before the objects and `manifest.json` are bundled
into `silo-DDMMYYYY.zip` for FTP. Only the temporary ZIP is removed afterwards;
Silo manages retention of its own backups. A backup completed with warnings is
uploaded with a warning on standard error; incomplete or failed backups are not.

Configure backup storage in Silo first. Native backups require the PostgreSQL
user store and compatible `pg_dump`/`pg_restore` tools on the Silo server.
The timeout covers creation and downloading (default 3600 seconds); polling is
every 5 seconds by default. Both settings must be positive. A timeout or a
connection failure does not stop a backup already started on Silo; the module
does not automatically retry the creation request.

To restore the whole server, extract the ZIP into a folder and follow
[Silo's restore instructions](https://siloserver.org/docs/restore) using that
folder with `silo backup restore --from`. Backups from S3 remain encrypted:
keep the corresponding `SECRET_KEY` separately. Silo's native backups exclude
your media, `.env`, Compose files, and `SECRET_KEY`; preserve those separately
as described in the [backup documentation](https://siloserver.org/docs/backups).

## Secrets

Secrets are not stored in the JSON configuration. The values
`passwordCredential`, `apiKeyCredential`, and `tokenCredential` are names of
systemd credentials.

Create the encrypted credential directory:

```sh
sudo install -d -m 0700 /etc/credstore.encrypted
```

Create each encrypted credential. Each command reads the secret from standard
input:

```sh
sudo systemd-creds encrypt --name=ftp_password \
  - /etc/credstore.encrypted/ftp_password

sudo systemd-creds encrypt --name=silo_api_key \
  - /etc/credstore.encrypted/silo_api_key

sudo systemd-creds encrypt --name=homeassistant_token \
  - /etc/credstore.encrypted/homeassistant_token
```

Load the credentials required by the configured modules in the systemd service:

```ini
LoadCredentialEncrypted=ftp_password:/etc/credstore.encrypted/ftp_password
LoadCredentialEncrypted=silo_api_key:/etc/credstore.encrypted/silo_api_key
LoadCredentialEncrypted=homeassistant_token:/etc/credstore.encrypted/homeassistant_token
```

The supplied [`deploy/plex-backup.service`](deploy/plex-backup.service) contains
a complete service example. Adapt its paths, service account, and credential
list before enabling it.
