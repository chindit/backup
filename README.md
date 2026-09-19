# PlexBackup

PlexBackup creates or retrieves backups for Plex, Jellyfin, and Home Assistant,
then uploads them to a plain FTP server.

## Usage

Run every module present in the configuration:

```sh
dotnet PlexBackup.dll backup --config /etc/backup.json
```

Run only one module:

```sh
dotnet PlexBackup.dll backup --config /etc/backup.json --module jellyfin
```

Select several modules by repeating the option:

```sh
dotnet PlexBackup.dll backup --config /etc/backup.json \
  --module plex \
  --module homeassistant
```

Available module names are `plex`, `jellyfin`, and `homeassistant`. There is no
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
    "jellyfin": {
      "server": "http://127.0.0.1:8096",
      "apiKeyCredential": "jellyfin_api_key",
      "metadata": true,
      "subtitles": true,
      "trickplay": false
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
- be able to read backups created by Jellyfin;
- be able to write to each `tempDirectory`.

The Jellyfin API key must have administrator access. Home Assistant requires a
long-lived access token belonging to an administrator, and its URL must use
HTTPS.

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

sudo systemd-creds encrypt --name=jellyfin_api_key \
  - /etc/credstore.encrypted/jellyfin_api_key

sudo systemd-creds encrypt --name=homeassistant_token \
  - /etc/credstore.encrypted/homeassistant_token
```

Load the credentials required by the configured modules in the systemd service:

```ini
LoadCredentialEncrypted=ftp_password:/etc/credstore.encrypted/ftp_password
LoadCredentialEncrypted=jellyfin_api_key:/etc/credstore.encrypted/jellyfin_api_key
LoadCredentialEncrypted=homeassistant_token:/etc/credstore.encrypted/homeassistant_token
```

The supplied [`deploy/plex-backup.service`](deploy/plex-backup.service) contains
a complete service example. Adapt its paths, service account, and credential
list before enabling it.
