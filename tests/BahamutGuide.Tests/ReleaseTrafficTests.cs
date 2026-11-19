using BahamutGuide;
using System;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

internal static partial class Program
{
    private sealed class ReleaseTransport : HttpMessageHandler
    {
        internal int Heads, Api, Assets, HeadStatus = 302, ApiStatus = 200;
        internal string Location, Metadata, Hash;
        internal byte[] Zip;
        internal bool Offline;
        internal ReleaseTransport(string tag)
        {
            Location = UpdateConfiguration.RepositoryUrl + "/releases/tag/" + tag;
            Metadata = "{\"tag_name\":\"" + tag + "\",\"draft\":false,\"prerelease\":false,\"body\":\"Notes\"}";
        }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var url = request.RequestUri;
            if (request.Method == HttpMethod.Head && url.AbsoluteUri == UpdateConfiguration.RepositoryUrl + "/releases/latest")
            {
                Heads++;
                if (Offline) throw new HttpRequestException("offline");
                var head = new HttpResponseMessage((HttpStatusCode)HeadStatus);
                head.Headers.Location = new Uri(Location, UriKind.RelativeOrAbsolute);
                return Task.FromResult(head);
            }
            if (url.Host == "api.github.com")
            {
                Api++;
                var response = new HttpResponseMessage((HttpStatusCode)ApiStatus) { Content = new StringContent(Metadata) };
                response.Headers.ETag = new System.Net.Http.Headers.EntityTagHeaderValue("\"tag-metadata\"");
                if (ApiStatus == 304) Check(request.Headers.IfNoneMatch.Count == 1, "conditional tag metadata");
                return Task.FromResult(response);
            }
            Assets++;
            HttpContent asset;
            if (url.AbsolutePath.EndsWith(".zip")) asset = new ByteArrayContent(Zip);
            else asset = new StringContent(Hash + "  " + Path.GetFileNameWithoutExtension(url.AbsolutePath) + ".zip");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = asset });
        }
    }

    private static void UpdateTraffic()
    {
        var current = typeof(PluginSettings).Assembly.GetName().Version;
        var tag = "v" + current.ToString(3);
        var token = CancellationToken.None;
        var transport = new ReleaseTransport(tag);
        using (var service = new UpdateService(transport))
        {
            var now = DateTime.UtcNow;
            service.UtcNow = () => now;
            var same = service.CheckAsync(current, token).Result;
            service.CheckAsync(current, token).Wait();
            Check(same.Status == UpdateCheckStatus.UpToDate && transport.Heads == 1 && transport.Api == 0 && transport.Assets == 0, "same version and cached checks use zero API");
            now = now.AddMinutes(2);
            service.CheckAsync(current, token).Wait();
            Check(transport.Heads == 2 && transport.Api == 0, "current version uses only HEAD after expiry");
            var older = new Version(0,0,0);
            var update = service.CheckAsync(older, token).Result;
            Check(update.Status == UpdateCheckStatus.UpdateAvailable && update.Release.Notes == "Notes" && transport.Api == 1, "newer stable tag gets notes once");
            now = now.AddMinutes(2);
            transport.ApiStatus = 304;
            Check(service.CheckAsync(older, token).Result.Release.Notes == "Notes", "304 retains matching notes");
        }

        foreach (var invalid in new[] { "https://github.com/other/project/releases/tag/" + tag,
            UpdateConfiguration.RepositoryUrl + "/releases/tag/v1.2.3-rc.1",
            UpdateConfiguration.RepositoryUrl + "/releases/tag/v1.2.3?download=1" })
        {
            transport = new ReleaseTransport(tag) { Location = invalid };
            using (var service = new UpdateService(transport))
                Check(service.CheckAsync(current, token).Result.Status == UpdateCheckStatus.Failed && transport.Api == 0, "invalid redirect rejected");
        }
        foreach (bool offline in new[] { false, true })
        {
            transport = new ReleaseTransport(tag) { HeadStatus = 429, Offline = offline };
            using (var service = new UpdateService(transport))
            {
                var result = service.CheckAsync(current, token).Result;
                service.CheckAsync(current, token).Wait();
                Check(result.Status == UpdateCheckStatus.Failed && result.Error == (offline ? "CheckOffline" : "CheckLimited") && transport.Heads == 1 && transport.Api == 0, "distinguish HEAD limit and offline without retry");
            }
        }
        transport = new ReleaseTransport(tag) { Metadata = "{\"tag_name\":\"v2.0.0\",\"draft\":false,\"prerelease\":false}" };
        using (var service = new UpdateService(transport))
            Check(service.CheckAsync(new Version(0,0,0), token).Result.Status == UpdateCheckStatus.Failed, "API metadata must match verified tag");

        var zipPath = Path.Combine(artifacts,"fallback.zip");
        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            zip.CreateEntryFromFile(typeof(PluginSettings).Assembly.Location, UpdateConfiguration.PluginFileName);
            zip.CreateEntryFromFile(typeof(BahamutGuide.Updater.Program).Assembly.Location, UpdateConfiguration.UpdaterFileName);
            zip.CreateEntry("README.md");
        }
        foreach (var code in new[] {403,429,503})
        {
            transport = new ReleaseTransport(tag) { ApiStatus = code, Zip = File.ReadAllBytes(zipPath), Hash = UpdatePackageVerifier.ComputeSha256(zipPath) };
            using (var service = new UpdateService(transport))
            {
                var result = service.CheckAsync(new Version(0,0,0), token).Result;
                Check(result.Status == UpdateCheckStatus.UpdateAvailable && result.Error == (code == 503 ? "NotesOffline" : "NotesLimited") && transport.Assets == 0, "limit preserves update without implicit download");
                var prefix = UpdateConfiguration.RepositoryUrl + "/releases/download/"+tag+"/BahamutGuide-"+tag;
                Check(result.Release.Assets.TrueForAll(a=>a.DownloadUrl==prefix+".zip" || a.DownloadUrl==prefix+".sha256"), "fallback only pins official assets");
                var pkg = service.DownloadAndVerifyAsync(result.Release, token).GetAwaiter().GetResult();
                Check(File.Exists(pkg.StagedUpdaterPath) && transport.Assets == 2, "explicit fallback verifies real package and helper");
                transport.Zip[0] ^= 1;
                Throws(()=>service.DownloadAndVerifyAsync(result.Release, token).GetAwaiter().GetResult(), "corrupt limited-mode asset rejected");
            }
        }
    }
}
