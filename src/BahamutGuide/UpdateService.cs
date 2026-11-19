using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace BahamutGuide
{
    internal sealed class UpdateService : IDisposable
    {
        private const long MaximumDownloadBytes = 100L * 1024 * 1024;
        private readonly HttpClient client;
        private readonly SemaphoreSlim checkGate = new SemaphoreSlim(1, 1);
        private Version cachedCurrent;
        private UpdateCheckResult cachedCheck;
        private System.Net.Http.Headers.EntityTagHeaderValue releaseEtag;
        private DateTime cacheExpires;
        internal Func<DateTime> UtcNow = () => DateTime.UtcNow;

        public UpdateService() : this(new HttpClientHandler { AllowAutoRedirect = false }) { }

        internal UpdateService(HttpMessageHandler handler)
        {
            client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15), MaxResponseContentBufferSize = 1024 * 1024 };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("BahamutGuide/" + Assembly.GetExecutingAssembly().GetName().Version.ToString(3));
            client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        }

        public async Task<UpdateCheckResult> CheckAsync(Version currentVersion, CancellationToken cancellationToken)
        {
            if (!UpdateConfiguration.IsConfigured)
                return new UpdateCheckResult { Status = UpdateCheckStatus.RepositoryNotConfigured };
            await checkGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (cachedCheck != null && Equals(cachedCurrent, currentVersion) && UtcNow() < cacheExpires)
                    return cachedCheck;
                UpdateCheckResult result;
                try { result = await ProbeLatestAsync(currentVersion, cancellationToken).ConfigureAwait(false); }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                catch (HttpRequestException) { result = ConnectionFailed(); }
                catch (OperationCanceledException) { result = ConnectionFailed(); }
                catch (Exception) { result = new UpdateCheckResult { Status = UpdateCheckStatus.Failed }; }
                cachedCurrent = currentVersion;
                cachedCheck = result;
                cacheExpires = UtcNow().AddMinutes(1);
                return result;
            }
            finally { checkGate.Release(); }
        }

        private async Task<UpdateCheckResult> ProbeLatestAsync(Version current, CancellationToken token)
        {
            var latest = new Uri(UpdateConfiguration.RepositoryUrl + "/releases/latest");
            ReleaseInfo release;
            using (var head = new HttpRequestMessage(HttpMethod.Head, latest))
            using (var response = await client.SendAsync(head, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false))
            {
                int status = (int)response.StatusCode;
                if (IsLimited(status)) return new UpdateCheckResult { Status = UpdateCheckStatus.Failed, Error = "CheckLimited" };
                if (status == 404) return new UpdateCheckResult { Status = UpdateCheckStatus.NoStableRelease };
                if (status != 301 && status != 302 && status != 303 && status != 307 && status != 308)
                    throw new InvalidDataException("Expected stable release redirect.");
                var location = response.Headers.Location;
                if (location == null) throw new InvalidDataException("Missing stable release tag.");
                var tagUri = new Uri(latest, location);
                var prefix = UpdateConfiguration.RepositoryUrl + "/releases/tag/";
                if (!tagUri.AbsoluteUri.StartsWith(prefix, StringComparison.Ordinal)) throw new InvalidDataException("Unexpected release redirect.");
                var tag = tagUri.AbsoluteUri.Substring(prefix.Length);
                SemVersion version;
                if (!SemVersion.TryParse(tag, out version) || !version.IsStable || tag != "v" + version)
                    throw new InvalidDataException("Invalid stable release tag.");
                release = new ReleaseInfo { Tag = tag, Version = version };
            }

            if (new Version(release.Version.Major, release.Version.Minor, release.Version.Patch)
                <= new Version(current.Major, current.Minor, Math.Max(current.Build, 0)))
                return new UpdateCheckResult { Status = UpdateCheckStatus.UpToDate, Release = release };

            var api = new Uri(UpdateConfiguration.ReleaseTagApiPrefix + release.Tag);
            using (var request = new HttpRequestMessage(HttpMethod.Get, api))
            {
                bool sameTag = cachedCheck?.Status == UpdateCheckStatus.UpdateAvailable && cachedCheck.Release.Tag == release.Tag;
                if (sameTag && releaseEtag != null) request.Headers.IfNoneMatch.Add(releaseEtag);
                HttpResponseMessage details;
                try { details = await client.SendAsync(request, token).ConfigureAwait(false); }
                catch (HttpRequestException) { return FromPublicTag(release, "NotesOffline"); }
                catch (OperationCanceledException) when (!token.IsCancellationRequested) { return FromPublicTag(release, "NotesOffline"); }
                using (var response = details)
                {
                    if (response.StatusCode == System.Net.HttpStatusCode.NotModified && sameTag && releaseEtag != null)
                        return cachedCheck;
                    releaseEtag = null;
                    int status = (int)response.StatusCode;
                    if (IsLimited(status)) return FromPublicTag(release, "NotesLimited");
                    if (status >= 500) return FromPublicTag(release, "NotesOffline");
                    response.EnsureSuccessStatusCode();
                    var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    var metadata = ReleaseParser.ParseStableReleases("[" + json + "]").SingleOrDefault();
                    if (metadata == null || metadata.Tag != release.Tag) throw new InvalidDataException("Stable tag mismatch.");
                    releaseEtag = response.Headers.ETag;
                    return new UpdateCheckResult { Status = UpdateCheckStatus.UpdateAvailable, Release = metadata };
                }
            }
        }

        private static bool IsLimited(int status) => status == 403 || status == 429;

        private static UpdateCheckResult ConnectionFailed() =>
            new UpdateCheckResult { Status = UpdateCheckStatus.Failed, Error = "CheckOffline" };

        private UpdateCheckResult FromPublicTag(ReleaseInfo release, string messageKey)
        {
            releaseEtag = null;
            foreach (string extension in new[] { ".zip", ".sha256" })
            {
                var name = "BahamutGuide-" + release.Tag + extension;
                release.Assets.Add(new ReleaseAsset { Name = name, DownloadUrl = UpdateConfiguration.RepositoryUrl +
                    "/releases/download/" + release.Tag + "/" + name });
            }
            return new UpdateCheckResult { Status = UpdateCheckStatus.UpdateAvailable, Release = release, Error = messageKey };
        }

        public async Task<PreparedUpdate> DownloadAndVerifyAsync(ReleaseInfo release, CancellationToken cancellationToken)
        {
            using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                deadline.CancelAfter(TimeSpan.FromMinutes(2));
                return await DownloadAndVerifyCoreAsync(release, deadline.Token).ConfigureAwait(false);
            }
        }

        private async Task<PreparedUpdate> DownloadAndVerifyCoreAsync(ReleaseInfo release, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (release?.Version == null || !release.Version.IsStable || release.Tag != "v" + release.Version) throw new InvalidDataException("InvalidPackage");
            var expectedZip = "BahamutGuide-v" + release.Version + ".zip";
            var alternateZip = "BahamutGuide-" + release.Version + ".zip";
            var zip = release.Assets.FirstOrDefault(x => string.Equals(x.Name, expectedZip, StringComparison.OrdinalIgnoreCase) || string.Equals(x.Name, alternateZip, StringComparison.OrdinalIgnoreCase));
            var expectedManifest = "BahamutGuide-v" + release.Version + ".sha256";
            var manifest = release.Assets.FirstOrDefault(x => string.Equals(x.Name, expectedManifest, StringComparison.OrdinalIgnoreCase)) ??
                release.Assets.FirstOrDefault(x => string.Equals(x.Name, zip == null ? string.Empty : zip.Name + ".sha256", StringComparison.OrdinalIgnoreCase)) ??
                release.Assets.FirstOrDefault(x => string.Equals(x.Name, "SHA256SUMS.txt", StringComparison.OrdinalIgnoreCase));
            if (zip == null || manifest == null) throw new InvalidDataException("UpdateAssetMissing");
            var assetPrefix = "https://github.com/" + UpdateConfiguration.RepositoryOwner + "/" + UpdateConfiguration.RepositoryName + "/releases/download/" + release.Tag + "/";
            if (!string.Equals(zip.DownloadUrl, assetPrefix + zip.Name, StringComparison.Ordinal) || !string.Equals(manifest.DownloadUrl, assetPrefix + manifest.Name, StringComparison.Ordinal)) throw new InvalidDataException("InvalidPackage");
            if (zip.Size <= 0 || zip.Size > MaximumDownloadBytes || manifest.Size <= 0 || manifest.Size > 1024 * 1024) throw new InvalidDataException("InvalidPackage");

            var root = Path.Combine(Path.GetTempPath(), "BahamutGuideUpdater", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
            var zipPath = Path.Combine(root, "package.zip");
            var manifestPath = Path.Combine(root, "package.sha256");
            await DownloadAsync(manifest, manifestPath, 1024 * 1024, cancellationToken).ConfigureAwait(false);
            var expected = UpdatePackageVerifier.FindManifestHash(File.ReadAllText(manifestPath, Encoding.UTF8), zip.Name);
            if (expected == null) throw new InvalidDataException("HashFailed");
            await DownloadAsync(zip, zipPath, MaximumDownloadBytes, cancellationToken).ConfigureAwait(false);
            if (!UpdatePackageVerifier.VerifySha256(zipPath, expected)) throw new InvalidDataException("HashFailed");

            var extracted = Path.Combine(root, "extracted");
            cancellationToken.ThrowIfCancellationRequested();
            UpdatePackageVerifier.ExtractValidated(zipPath, extracted);
            var dll = Directory.GetFiles(extracted, UpdateConfiguration.PluginFileName, SearchOption.AllDirectories).SingleOrDefault();
            var updater = Directory.GetFiles(extracted, UpdateConfiguration.UpdaterFileName, SearchOption.AllDirectories).SingleOrDefault();
            if (dll == null || updater == null) throw new InvalidDataException("InvalidPackage");
            UpdatePackageVerifier.ValidatePluginAssembly(dll, release.Version);
            UpdatePackageVerifier.ValidateUpdaterAssembly(updater, release.Version);
            cancellationToken.ThrowIfCancellationRequested();
            return new PreparedUpdate { StagedDllPath = dll, StagedUpdaterPath = updater, UpdaterSha256 = UpdatePackageVerifier.ComputeSha256(updater), DllSha256 = UpdatePackageVerifier.ComputeSha256(dll), Release = release };
            }
            catch
            {
                try { Directory.Delete(root, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
                throw;
            }
        }

        private async Task DownloadAsync(ReleaseAsset asset, string destination, long limit, CancellationToken cancellationToken)
        {
            var uri = new Uri(asset.DownloadUrl);
            EnsureAllowedUri(uri, false);
            HttpResponseMessage response = null;
            for (var redirect = 0; redirect <= 5; redirect++)
            {
                response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
                var status = (int)response.StatusCode;
                if (status < 300 || status >= 400) break;
                var location = response.Headers.Location;
                response.Dispose(); response = null;
                if (location == null) throw new InvalidDataException("InvalidPackage");
                uri = location.IsAbsoluteUri ? location : new Uri(uri, location);
                EnsureAllowedUri(uri, false);
                if (uri.Host == "github.com" && uri.AbsoluteUri != asset.DownloadUrl) throw new InvalidDataException("Unexpected asset redirect.");
            }
            if (response == null) throw new InvalidDataException("InvalidPackage");
            using (response)
            {
                response.EnsureSuccessStatusCode();
                if (response.Content.Headers.ContentLength.HasValue && response.Content.Headers.ContentLength.Value > limit)
                    throw new InvalidDataException("InvalidPackage");
                using (var input = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                using (var output = File.Create(destination))
                {
                    var buffer = new byte[81920];
                    long total = 0;
                    int read;
                    while ((read = await input.ReadAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false)) > 0)
                    {
                        total += read;
                        if (total > limit) throw new InvalidDataException("InvalidPackage");
                        await output.WriteAsync(buffer, 0, read, cancellationToken).ConfigureAwait(false);
                    }
                    if (total == 0 || (asset.Size.HasValue && asset.Size.Value != total)) throw new InvalidDataException("Invalid asset length.");
                }
            }
        }

        internal static void EnsureAllowedUri(Uri uri, bool api)
        {
            if (uri == null || uri.Scheme != Uri.UriSchemeHttps || !uri.IsDefaultPort || !string.IsNullOrEmpty(uri.UserInfo)) throw new InvalidDataException("Only HTTPS update URLs are allowed.");
            var host = uri.Host.ToLowerInvariant();
            var allowed = api ? host == "api.github.com" :
                host == "github.com" || host == "objects.githubusercontent.com" || host == "release-assets.githubusercontent.com";
            if (!allowed) throw new InvalidDataException("Unexpected update host.");
        }

        public void Dispose() { client.Dispose(); checkGate.Dispose(); }
    }
}
