using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using PlexBackup.Resources;
using PlexBackup.Services;
using Xunit;

namespace PlexBackup.Test;

public sealed class FtpUploaderTests : IDisposable
{
    private readonly string _testDirectory = Path.Combine(
        Path.GetTempPath(),
        $"PlexBackup.FtpTests.{Guid.NewGuid():N}");

    [Fact]
    public async Task UploadAsync_UsesStorWithoutRemoteFileManagement()
    {
        Directory.CreateDirectory(_testDirectory);
        var localFile = new FileInfo(
            Path.Combine(_testDirectory, "backup.zip"));
        File.WriteAllText(localFile.FullName, "archive data");

        await using var server = new UploadOnlyFtpServer();
        var uploader = new FtpUploader(
            new StaticCredentialStore("secret"));
        var config = new FtpConfig
        {
            Server = $"ftp://127.0.0.1:{server.Port}/backups",
            Username = "backup-user",
            PasswordCredential = "ftp_password"
        };

        await uploader.UploadAsync(
            localFile,
            "plex-19092026.zip",
            config,
            CancellationToken.None);
        await server.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Contains(
            server.Commands,
            command => command == "STOR backups/plex-19092026.zip");
        string[] forbiddenCommands =
        [
            "APPE",
            "CWD",
            "DELE",
            "LIST",
            "MDTM",
            "MFMT",
            "MKD",
            "MLSD",
            "MLST",
            "NLST",
            "RNFR",
            "RNTO",
            "SIZE"
        ];
        Assert.DoesNotContain(
            server.Commands,
            command => forbiddenCommands.Any(
                forbidden => command.StartsWith(
                    forbidden,
                    StringComparison.OrdinalIgnoreCase)));
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDirectory))
        {
            Directory.Delete(_testDirectory, recursive: true);
        }
    }

    private sealed class StaticCredentialStore(
        string value) : ICredentialStore
    {
        public string GetRequired(string credentialName)
        {
            Assert.Equal("ftp_password", credentialName);
            return value;
        }
    }

    private sealed class UploadOnlyFtpServer : IAsyncDisposable
    {
        private readonly TcpListener _controlListener;
        private readonly CancellationTokenSource _stopping = new();
        private TcpListener? _dataListener;

        public UploadOnlyFtpServer()
        {
            _controlListener = new TcpListener(
                IPAddress.Loopback,
                0);
            _controlListener.Start();
            Port = ((IPEndPoint)_controlListener.LocalEndpoint).Port;
            Completion = ServeAsync();
        }

        public int Port { get; }
        public ConcurrentQueue<string> Commands { get; } = new();
        public Task Completion { get; }

        public async ValueTask DisposeAsync()
        {
            _stopping.Cancel();
            _controlListener.Stop();
            _dataListener?.Stop();
            try
            {
                await Completion;
            }
            catch (Exception exception) when (exception is
                OperationCanceledException or SocketException)
            {
            }

            _stopping.Dispose();
        }

        private async Task ServeAsync()
        {
            using TcpClient control = await _controlListener
                .AcceptTcpClientAsync(_stopping.Token);
            await using NetworkStream stream = control.GetStream();
            using var reader = new StreamReader(
                stream,
                Encoding.ASCII,
                detectEncodingFromByteOrderMarks: false,
                leaveOpen: true);
            await using var writer = new StreamWriter(
                stream,
                Encoding.ASCII,
                leaveOpen: true)
            {
                AutoFlush = true,
                NewLine = new string([(char)13, (char)10])
            };

            await writer.WriteLineAsync("220 Ready");
            while (!_stopping.IsCancellationRequested)
            {
                string? command = await reader.ReadLineAsync(
                    _stopping.Token);
                if (command is null)
                {
                    return;
                }

                Commands.Enqueue(command);
                string verb = command.Split(' ', 2)[0].ToUpperInvariant();
                switch (verb)
                {
                    case "USER":
                        await writer.WriteLineAsync("331 Password required");
                        break;
                    case "PASS":
                        await writer.WriteLineAsync("230 Logged in");
                        break;
                    case "EPSV":
                        StartDataListener();
                        int epsvPort = ((IPEndPoint)
                            _dataListener!.LocalEndpoint).Port;
                        await writer.WriteLineAsync(
                            $"229 Entering Extended Passive Mode (|||{epsvPort}|)");
                        break;
                    case "PASV":
                        StartDataListener();
                        int pasvPort = ((IPEndPoint)
                            _dataListener!.LocalEndpoint).Port;
                        await writer.WriteLineAsync(
                            $"227 Entering Passive Mode (127,0,0,1,{pasvPort / 256},{pasvPort % 256})");
                        break;
                    case "STOR":
                        await writer.WriteLineAsync(
                            "150 Opening data connection");
                        using (TcpClient data = await _dataListener!
                                   .AcceptTcpClientAsync(_stopping.Token))
                        {
                            await data.GetStream().CopyToAsync(
                                Stream.Null,
                                _stopping.Token);
                        }

                        _dataListener.Stop();
                        _dataListener = null;
                        await writer.WriteLineAsync("226 Transfer complete");
                        break;
                    case "QUIT":
                        await writer.WriteLineAsync("221 Goodbye");
                        return;
                    default:
                        await writer.WriteLineAsync("200 OK");
                        break;
                }
            }
        }

        private void StartDataListener()
        {
            _dataListener?.Stop();
            _dataListener = new TcpListener(
                IPAddress.Loopback,
                0);
            _dataListener.Start();
        }
    }
}
