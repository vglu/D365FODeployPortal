using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DeployPortal.Models.Api;
using Microsoft.AspNetCore.Mvc.Testing;
using NUnit.Framework;

namespace DeployPortal.Tests;

/// <summary>
/// REST API tests using WebApplicationFactory. Covers GET/POST endpoints for packages,
/// upload, convert, merge, refresh-licenses, download, and licenses.
/// </summary>
[TestFixture]
public class ApiTests
{
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;
    private string _testDir = "";

    [SetUp]
    public void Setup()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"deploy-portal-api-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);
        var dbPath = Path.Combine(_testDir, "test.db");
        var storagePath = Path.Combine(_testDir, "packages");
        var tempDir = Path.Combine(_testDir, "temp");
        var keysDir = Path.Combine(_testDir, "keys");
        Directory.CreateDirectory(storagePath);
        Directory.CreateDirectory(tempDir);
        Directory.CreateDirectory(keysDir);

        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("DeployPortal:DatabasePath", dbPath);
                builder.UseSetting("DeployPortal:PackageStoragePath", storagePath);
                builder.UseSetting("DeployPortal:TempWorkingDir", tempDir);
                builder.UseSetting("DeployPortal:DataProtectionKeysPath", keysDir);
                builder.UseSetting("DeployPortal:ConverterEngine", "BuiltIn");
            });
        _client = _factory.CreateClient();
    }

    [TearDown]
    public void Teardown()
    {
        _client?.Dispose();
        _factory?.Dispose();
        try { Directory.Delete(_testDir, true); } catch { }
    }

    [Test]
    public async Task GetPackages_ReturnsEmptyArray_WhenNoPackages()
    {
        var response = await _client.GetAsync("/api/packages");
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync();
        var list = JsonSerializer.Deserialize<List<PackageDto>>(json);
        Assert.That(list, Is.Not.Null);
        Assert.That(list!, Is.Empty);
    }

    [Test]
    public async Task GetPackage_Returns404_WhenNotFound()
    {
        var response = await _client.GetAsync("/api/packages/99999");
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task Upload_AcceptsZip_Returns201AndPackage()
    {
        var zipPath = CreateMinimalLcsZip();
        var bytes = await File.ReadAllBytesAsync(zipPath);
        using var content = new MultipartFormDataContent();
        content.Add(new ByteArrayContent(bytes), "file", "TestLcs.zip");

        var response = await _client.PostAsync("/api/packages/upload", content);
        if (!response.IsSuccessStatusCode)
            Assert.Fail($"Upload failed: {response.StatusCode} - {await response.Content.ReadAsStringAsync()}");
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        var location = response.Headers.Location?.ToString();
        Assert.That(location, Does.Contain("/api/packages/"));
        var dto = await response.Content.ReadFromJsonAsync<PackageDto>();
        Assert.That(dto, Is.Not.Null);
        Assert.That(dto!.Id, Is.GreaterThan(0));
        Assert.That(dto.Name, Is.Not.Empty);
        Assert.That(dto.PackageType, Is.EqualTo("LCS"));
    }

    [Test]
    public async Task Upload_RejectsNonZip_Returns400()
    {
        using var content = new MultipartFormDataContent();
        content.Add(new StringContent("not a zip"), "file", "readme.txt");

        var response = await _client.PostAsync("/api/packages/upload", content);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task GetPackage_ReturnsPackage_AfterUpload()
    {
        var zipPath = CreateMinimalLcsZip();
        var bytes = await File.ReadAllBytesAsync(zipPath);
        using (var content = new MultipartFormDataContent())
        {
            content.Add(new ByteArrayContent(bytes), "file", "GetTest.zip");
            await _client.PostAsync("/api/packages/upload", content);
        }

        var listResponse = await _client.GetAsync("/api/packages");
        listResponse.EnsureSuccessStatusCode();
        var list = await listResponse.Content.ReadFromJsonAsync<List<PackageDto>>();
        Assert.That(list, Is.Not.Null.And.Count.GreaterThan(0));
        var id = list![0].Id;

        var getResponse = await _client.GetAsync($"/api/packages/{id}");
        getResponse.EnsureSuccessStatusCode();
        var dto = await getResponse.Content.ReadFromJsonAsync<PackageDto>();
        Assert.That(dto, Is.Not.Null);
        Assert.That(dto!.Id, Is.EqualTo(id));
    }

    [Test]
    [Explicit("Requires UnifiedTemplate in DeployPortal build output")]
    public async Task ConvertToUnified_Returns200AndNewPackage_ForLcs()
    {
        var zipPath = CreateNestedMinimalLcsZip();
        var bytes = await File.ReadAllBytesAsync(zipPath);
        int id;
        using (var content = new MultipartFormDataContent())
        {
            content.Add(new ByteArrayContent(bytes), "file", "ConvertLcs.zip");
            var upload = await _client.PostAsync("/api/packages/upload", content);
            upload.EnsureSuccessStatusCode();
            var dto = await upload.Content.ReadFromJsonAsync<PackageDto>();
            id = dto!.Id;
        }

        var response = await _client.PostAsync($"/api/packages/{id}/convert/unified", null);
        if (!response.IsSuccessStatusCode)
            Assert.Fail($"Convert to Unified failed: {response.StatusCode} - {await response.Content.ReadAsStringAsync()}");
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var result = await response.Content.ReadFromJsonAsync<PackageDto>();
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.PackageType, Is.EqualTo("Unified"));
        Assert.That(result.Id, Is.Not.EqualTo(id));
    }

    [Test]
    public async Task ConvertToUnified_Returns400_WhenPackageIsUnified()
    {
        var zipPath = CreateMinimalUnifiedZip();
        var bytes = await File.ReadAllBytesAsync(zipPath);
        int id;
        using (var content = new MultipartFormDataContent())
        {
            content.Add(new ByteArrayContent(bytes), "file", "Unified.zip");
            var upload = await _client.PostAsync("/api/packages/upload", content);
            upload.EnsureSuccessStatusCode();
            id = (await upload.Content.ReadFromJsonAsync<PackageDto>())!.Id;
        }

        var response = await _client.PostAsync($"/api/packages/{id}/convert/unified", null);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    [Explicit("Requires UnifiedTemplate in DeployPortal build output")]
    public async Task ConvertToLcs_Returns200_ForUnifiedPackage()
    {
        var zipPath = CreateNestedMinimalLcsZip();
        var bytes = await File.ReadAllBytesAsync(zipPath);
        int lcsId;
        using (var content = new MultipartFormDataContent())
        {
            content.Add(new ByteArrayContent(bytes), "file", "LcsForRoundTrip.zip");
            var upload = await _client.PostAsync("/api/packages/upload", content);
            upload.EnsureSuccessStatusCode();
            lcsId = (await upload.Content.ReadFromJsonAsync<PackageDto>())!.Id;
        }

        var convertToUnified = await _client.PostAsync($"/api/packages/{lcsId}/convert/unified", null);
        convertToUnified.EnsureSuccessStatusCode();
        var unified = await convertToUnified.Content.ReadFromJsonAsync<PackageDto>();
        Assert.That(unified, Is.Not.Null);

        var response = await _client.PostAsync($"/api/packages/{unified!.Id}/convert/lcs", null);
        if (!response.IsSuccessStatusCode)
            Assert.Fail($"Convert to LCS failed: {response.StatusCode} - {await response.Content.ReadAsStringAsync()}");
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var backToLcs = await response.Content.ReadFromJsonAsync<PackageDto>();
        Assert.That(backToLcs, Is.Not.Null);
        Assert.That(backToLcs!.PackageType, Is.EqualTo("LCS"));
    }

    [Test]
    public async Task Merge_Returns201_WhenTwoPackagesExist()
    {
        var zipPath = CreateMinimalLcsZip();
        var bytes = await File.ReadAllBytesAsync(zipPath);
        var ids = new List<int>();
        for (int i = 0; i < 2; i++)
        {
            using var content = new MultipartFormDataContent();
            content.Add(new ByteArrayContent(bytes), "file", $"MergePkg{i}.zip");
            var upload = await _client.PostAsync("/api/packages/upload", content);
            upload.EnsureSuccessStatusCode();
            var dto = await upload.Content.ReadFromJsonAsync<PackageDto>();
            ids.Add(dto!.Id);
        }

        var body = new MergeRequestDto { PackageIds = ids, MergeName = "ApiMergeTest" };
        var response = await _client.PostAsJsonAsync("/api/packages/merge", body);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        var result = await response.Content.ReadFromJsonAsync<PackageDto>();
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.PackageType, Is.EqualTo("Merged"));
    }

    [Test]
    public async Task Merge_Returns400_WhenLessThanTwoIds()
    {
        var body = new MergeRequestDto { PackageIds = new List<int> { 1 } };
        var response = await _client.PostAsJsonAsync("/api/packages/merge", body);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task RefreshLicenses_Returns200_WithCount()
    {
        var zipPath = CreateMinimalLcsZip();
        var bytes = await File.ReadAllBytesAsync(zipPath);
        int id;
        using (var content = new MultipartFormDataContent())
        {
            content.Add(new ByteArrayContent(bytes), "file", "LicensePkg.zip");
            var upload = await _client.PostAsync("/api/packages/upload", content);
            upload.EnsureSuccessStatusCode();
            id = (await upload.Content.ReadFromJsonAsync<PackageDto>())!.Id;
        }

        var response = await _client.PostAsync($"/api/packages/{id}/refresh-licenses", null);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var json = await response.Content.ReadAsStringAsync();
        Assert.That(json, Does.Contain("count"));
    }

    [Test]
    public async Task Download_ReturnsZipFile_AfterUpload()
    {
        var zipPath = CreateMinimalLcsZip();
        var bytes = await File.ReadAllBytesAsync(zipPath);
        int id;
        using (var content = new MultipartFormDataContent())
        {
            content.Add(new ByteArrayContent(bytes), "file", "DownloadMe.zip");
            var upload = await _client.PostAsync("/api/packages/upload", content);
            upload.EnsureSuccessStatusCode();
            id = (await upload.Content.ReadFromJsonAsync<PackageDto>())!.Id;
        }

        var response = await _client.GetAsync($"/api/packages/{id}/download");
        response.EnsureSuccessStatusCode();
        Assert.That(response.Content.Headers.ContentType?.MediaType, Is.EqualTo("application/zip"));
        var responseBytes = await response.Content.ReadAsByteArrayAsync();
        Assert.That(responseBytes.Length, Is.GreaterThan(0));
    }

    [Test]
    public async Task Licenses_Returns200Or404_ForPackage()
    {
        var zipPath = CreateMinimalLcsZip();
        var bytes = await File.ReadAllBytesAsync(zipPath);
        int id;
        using (var content = new MultipartFormDataContent())
        {
            content.Add(new ByteArrayContent(bytes), "file", "LicensesPkg.zip");
            var upload = await _client.PostAsync("/api/packages/upload", content);
            upload.EnsureSuccessStatusCode();
            id = (await upload.Content.ReadFromJsonAsync<PackageDto>())!.Id;
        }

        var response = await _client.GetAsync($"/api/packages/{id}/licenses");
        // 200 when package has license files, 404 when none (minimal zip has no licenses)
        Assert.That(response.StatusCode, Is.AnyOf(HttpStatusCode.OK, HttpStatusCode.NotFound));
    }

    [Test]
    public async Task Swagger_Returns200()
    {
        var response = await _client.GetAsync("/swagger/v1/swagger.json");
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync();
        Assert.That(json, Does.Contain("openapi"));
        Assert.That(json, Does.Contain("D365FO Deploy Portal API"));
    }

    private string CreateMinimalLcsZip()
    {
        var path = Path.Combine(_testDir, "minimal_lcs.zip");
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            var xml = @"<?xml version=""1.0""?><HotfixInstallationInfo><MetadataModuleList><string>ModA</string></MetadataModuleList><AllComponentList><ArrayOfString><string>C</string></ArrayOfString></AllComponentList></HotfixInstallationInfo>";
            var entry = zip.CreateEntry("HotfixInstallationInfo.xml");
            using (var w = new StreamWriter(entry.Open()))
                w.Write(xml);
            var aos = zip.CreateEntry("AOSService/Packages/ModA.nupkg");
            using (var s = aos.Open())
                s.Write(new byte[] { 1, 2, 3 }, 0, 3);
        }
        return path;
    }

    private string CreateMinimalUnifiedZip()
    {
        var path = Path.Combine(_testDir, "minimal_unified.zip");
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            var dll = zip.CreateEntry("TemplatePackage.dll");
            using (var s = dll.Open())
                s.Write(new byte[] { 0x4D, 0x5A }, 0, 2); // minimal PE header
            AddZipEntry(zip, "PackageAssets/ImportConfig.xml", "<configdatastorage><externalpackages></externalpackages></configdatastorage>");
        }
        return path;
    }

    /// <summary>
    /// Creates a nested LCS zip (one root folder) so the built-in converter can process it.
    /// </summary>
    private string CreateNestedMinimalLcsZip()
    {
        var rootFolder = "AX_Minimal_Test_1.0.0.0";
        var path = Path.Combine(_testDir, "nested_lcs.zip");
        var moduleZipPath = Path.Combine(_testDir, "dynamicsax-ModuleA.1.0.0.0.zip");
        using (var mz = ZipFile.Open(moduleZipPath, ZipArchiveMode.Create))
        {
            var e = mz.CreateEntry("ModuleA/metadata.xml");
            using var w = new StreamWriter(e.Open());
            w.Write("<metadata/>");
        }

        using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            var hotfix = $@"<?xml version=""1.0""?><HotfixInstallationInfo><PlatformVersion>10.0.0.0</PlatformVersion><MetadataModuleList><string>ModuleA</string></MetadataModuleList></HotfixInstallationInfo>";
            AddZipEntry(zip, $"{rootFolder}/HotfixInstallationInfo.xml", hotfix);
            var moduleBytes = File.ReadAllBytes(moduleZipPath);
            var entry = zip.CreateEntry($"{rootFolder}/AOSService/Packages/files/dynamicsax-ModuleA.1.0.0.0.zip");
            using (var s = entry.Open())
                s.Write(moduleBytes, 0, moduleBytes.Length);
        }
        File.Delete(moduleZipPath);
        return path;
    }

    private static void AddZipEntry(ZipArchive zip, string entryName, string content)
    {
        var entry = zip.CreateEntry(entryName);
        using var writer = new StreamWriter(entry.Open());
        writer.Write(content);
    }
}
