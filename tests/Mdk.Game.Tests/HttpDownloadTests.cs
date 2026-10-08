using System.Net;
using System.Net.Sockets;
using System.Text;
using Mdk.Engine.Platform;

namespace Mdk.Game.Tests;

/// <summary>The upscaler's download through the OS's HTTP library (WinHTTP, libcurl): URLs, the
/// backend per OS, errors, and real transfers from a local server.</summary>
public class HttpDownloadTests
{
    private const string Body = "the upscaler's release";

    [Fact]
    public void CracksHttpsUrls()
    {
        var target = HttpTarget.Of(new Uri("https://github.com/x/releases/download/v1/a.zip?b=1"));

        Assert.Equal(new HttpTarget("github.com", 443, "/x/releases/download/v1/a.zip?b=1", Transport.Tls), target);
    }

    [Fact]
    public void CracksHttpUrlsWithPorts()
    {
        var target = HttpTarget.Of(new Uri("http://localhost:8080/"));

        Assert.Equal(new HttpTarget("localhost", 8080, "/", Transport.Plain), target);
    }

    [Fact]
    public void RefusesOtherSchemes()
    {
        Assert.Throws<ArgumentException>(() => HttpTarget.Of(new Uri("ftp://example.org/a.zip")));
    }

    // Names: the enums are internal to the engine.
    [Theory]
    [InlineData("Windows", "WinHttp")]
    [InlineData("Linux", "Curl")]
    [InlineData("MacOs", "Curl")]
    [InlineData("Other", "Curl")]
    [InlineData("Android", "None")]
    public void PicksTheOsLibrary(string os, string backend)
    {
        Assert.Equal(Enum.Parse<HttpBackend>(backend), HttpDownload.BackendOf(Enum.Parse<HostOs>(os)));
    }

    [Fact]
    public void CurlFollowsRedirectsAndFailsOnErrors()
    {
        var settings = Curl.Settings(new Uri("https://example.org/a.zip"));

        Assert.Contains(new CurlSetting(CurlOption.Url, 0, "https://example.org/a.zip"), settings);
        Assert.Contains(new CurlSetting(CurlOption.FollowLocation, 1, null), settings);
        Assert.Contains(new CurlSetting(CurlOption.FailOnError, 1, null), settings);
        Assert.Contains(new CurlSetting(CurlOption.NoProgress, 0, null), settings);
        Assert.Contains(settings, s => s.Option == CurlOption.UserAgent && s.Text?.Length > 0);
    }

    [Theory]
    [InlineData(22, 404, "HTTP response code said error", "HTTP 404")]
    [InlineData(6, 0, "Couldn't resolve host name", "Couldn't resolve host name")]
    public void CurlErrorsAreNamed(int code, long status, string text, string expected)
    {
        Assert.Equal(expected, Curl.Describe(code, status, text));
    }

    [Theory]
    [InlineData(12007, "host not found")]
    [InlineData(12029, "can't connect")]
    [InlineData(12175, "secure connection failed")]
    [InlineData(12345, "WinHTTP error 12345")]
    public void WinHttpErrorsAreNamed(int code, string expected)
    {
        Assert.Equal(expected, WinHttp.Describe(code));
    }

    [Fact]
    public void SavesFollowingRedirects()
    {
        using var server = new TestServer();
        var path = Path.GetTempFileName();
        long done = 0, total = 0;
        try
        {
            HttpDownload.Save(server.Url("redirect"), path, (d, t) => (done, total) = (d, t), CancellationToken.None);

            Assert.Equal(Body, File.ReadAllText(path));
            Assert.Equal(Body.Length, done);
            Assert.Equal(Body.Length, total);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void NamesTheStatusOfAFailure()
    {
        using var server = new TestServer();
        var path = Path.GetTempFileName();
        try
        {
            var error = Assert.Throws<IOException>(() => HttpDownload.Save(server.Url("missing"), path, null, CancellationToken.None));

            Assert.Contains("HTTP 404", error.Message);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Cancels()
    {
        using var server = new TestServer();
        var path = Path.GetTempFileName();
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        try
        {
            Assert.ThrowsAny<OperationCanceledException>(() => HttpDownload.Save(server.Url("file"), path, null, cancel.Token));
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>A one-request-per-connection HTTP/1.1 server on localhost:
    /// /redirect ─► 302 /file ─► 200 <see cref="Body"/>; anything else 404.</summary>
    private sealed class TestServer : IDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly Task _loop;

        public TestServer()
        {
            _listener.Start();
            _loop = Task.Run(Serve);
        }

        private int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

        public string Url(string path) => $"http://127.0.0.1:{Port}/{path}";

        private async Task Serve()
        {
            while (true)
            {
                TcpClient client;
                try
                {
                    client = await _listener.AcceptTcpClientAsync();
                }
                catch (Exception e) when (e is SocketException or ObjectDisposedException or InvalidOperationException)
                {
                    return;
                }

                using (client)
                {
                    Answer(client.GetStream());
                }
            }
        }

        private void Answer(NetworkStream stream)
        {
            var request = new StringBuilder();
            var buffer = new byte[1024];
            while (!request.ToString().Contains("\r\n\r\n"))
            {
                var read = stream.Read(buffer);
                if (read == 0)
                {
                    return;
                }

                request.Append(Encoding.ASCII.GetString(buffer, 0, read));
            }

            var path = request.ToString().Split(' ')[1];
            var response = path switch
            {
                "/redirect" => $"HTTP/1.1 302 Found\r\nLocation: http://127.0.0.1:{Port}/file\r\nContent-Length: 0\r\nConnection: close\r\n\r\n",
                "/file" => $"HTTP/1.1 200 OK\r\nContent-Length: {Body.Length}\r\nConnection: close\r\n\r\n{Body}",
                _ => "HTTP/1.1 404 Not Found\r\nContent-Length: 0\r\nConnection: close\r\n\r\n",
            };
            stream.Write(Encoding.ASCII.GetBytes(response));
        }

        public void Dispose()
        {
            _listener.Stop();
            _loop.Wait();
        }
    }
}
