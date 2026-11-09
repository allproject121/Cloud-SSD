using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using CloudPlayX.App;
using CloudPlayX.Core;
using CloudPlayX.Desktop;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CloudPlayX.Tests;

public class MobileBridgeTests
{
    private static (AppHost Host, string TempDir) CreateTestHost(int port)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "cpx_bridge_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var paths = new AppPaths(tempDir);
        var host = AppHost.Create(paths);
        host.Settings.MobileBridge.Port = port;
        host.Settings.MobileBridge.PairingToken = "testtoken123";
        return (host, tempDir);
    }

    [Fact]
    public void QrCodeGenerator_Generates_Valid_Bitmap_For_PairingUrl()
    {
        string url = "http://192.168.1.15:8090/?token=testtoken123";
        var bmp = QrCodeGenerator.GenerateQrBitmap(url, 4);

        Assert.NotNull(bmp);
        Assert.True(bmp.PixelWidth > 0);
        Assert.True(bmp.PixelHeight > 0);
        Assert.True(bmp.IsFrozen);
    }

    [Fact]
    public void LocalMobileBridgeService_PreferredLocalIp_Returns_Valid_Address()
    {
        var ip = LocalMobileBridgeService.GetPreferredLocalIp();
        Assert.NotNull(ip);
        Assert.NotEmpty(ip);
        Assert.True(IPAddress.TryParse(ip, out _));
    }

    [Fact]
    public async Task MobileBridge_Starts_And_Serves_Status_And_Hud_Endpoints()
    {
        // Pick random free port > 9000
        int port = 9123;
        var (host, tempDir) = CreateTestHost(port);

        try
        {
            var service = new LocalMobileBridgeService(host, NullLogger<LocalMobileBridgeService>.Instance);
            service.Start();
            Assert.True(service.IsRunning);

            using var http = new HttpClient();
            string baseUrl = $"http://127.0.0.1:{port}";

            // 1. Status endpoint
            var statusResp = await http.GetAsync($"{baseUrl}/api/status");
            Assert.Equal(HttpStatusCode.OK, statusResp.StatusCode);
            var statusJson = await statusResp.Content.ReadAsStringAsync();
            using var statusDoc = JsonDocument.Parse(statusJson);
            Assert.True(statusDoc.RootElement.TryGetProperty("version", out var vProp));
            Assert.Equal("0.7.0", vProp.GetString());

            // 2. HUD endpoint
            var hudResp = await http.GetAsync($"{baseUrl}/api/hud");
            Assert.Equal(HttpStatusCode.OK, hudResp.StatusCode);
            var hudJson = await hudResp.Content.ReadAsStringAsync();
            using var hudDoc = JsonDocument.Parse(hudJson);
            Assert.True(hudDoc.RootElement.TryGetProperty("cacheHitRate", out var hitProp));
            Assert.True(hitProp.GetDouble() > 0);

            // 3. Pairing endpoint - invalid token
            var failPair = await http.PostAsync($"{baseUrl}/api/pair?token=wrong_token", null);
            Assert.Equal(HttpStatusCode.Forbidden, failPair.StatusCode);

            // 4. Pairing endpoint - valid token
            var okPair = await http.PostAsync($"{baseUrl}/api/pair?token=testtoken123&name=Pixel8", null);
            Assert.Equal(HttpStatusCode.OK, okPair.StatusCode);
            var pairJson = await okPair.Content.ReadAsStringAsync();
            using var pairDoc = JsonDocument.Parse(pairJson);
            Assert.True(pairDoc.RootElement.GetProperty("success").GetBoolean());
            Assert.Contains(host.Settings.MobileBridge.PairedDevices, d => d.Name == "Pixel8");

            // 5. App Shell fallback
            var shellResp = await http.GetAsync($"{baseUrl}/");
            Assert.Equal(HttpStatusCode.OK, shellResp.StatusCode);

            service.Stop();
            Assert.False(service.IsRunning);
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    [Fact]
    public async Task MobileBridge_StreamEndpoint_Supports_Range_Requests()
    {
        int port = 9124;
        var (host, tempDir) = CreateTestHost(port);

        try
        {
            // Create dummy file for streaming test
            string testFile = Path.Combine(tempDir, "sample_clip.mp4");
            byte[] dummyData = new byte[1024];
            for (int i = 0; i < dummyData.Length; i++) dummyData[i] = (byte)(i % 256);
            await File.WriteAllBytesAsync(testFile, dummyData);

            var service = new LocalMobileBridgeService(host, NullLogger<LocalMobileBridgeService>.Instance);
            service.Start();

            using var http = new HttpClient();
            string streamUrl = $"http://127.0.0.1:{port}/api/stream?path={Uri.EscapeDataString(testFile)}&token=testtoken123";

            // Request first 64 bytes via Range header
            var req = new HttpRequestMessage(HttpMethod.Get, streamUrl);
            req.Headers.Range = new RangeHeaderValue(0, 63);

            var resp = await http.SendAsync(req);
            Assert.Equal(HttpStatusCode.PartialContent, resp.StatusCode);
            Assert.Equal(64, resp.Content.Headers.ContentLength);
            var received = await resp.Content.ReadAsByteArrayAsync();
            Assert.Equal(64, received.Length);
            Assert.Equal(0, received[0]);
            Assert.Equal(63, received[63]);

            service.Stop();
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }
}
