using NexInstaller.Infrastructure.Packages;
using NexInstaller.Core.Packages;
using System.Text.Json.Nodes;

namespace NexInstaller.Tests;

public sealed class ManifestTests
{
    internal static string ValidJson(string id = "editor") => $$"""
    {"schemaVersion":1,"id":"{{id}}","name":"Offline Editor","version":"1.0","architecture":"X64",
    "category":"Utilities","installerType":"Exe","entryPoint":"packages/editor/setup.exe",
    "files":[{"path":"packages/editor/setup.exe","sha256":"{{new string('a', 64)}}"}]}
    """;

    [Fact]
    public void ParsesMetadataAndDefaultsToManual()
    {
        var result = new JsonPackageManifestReader().Parse(ValidJson());
        Assert.True(result.IsValid);
        Assert.Equal(InstallationMode.Manual, result.Manifest!.InstallationMode);
        Assert.Equal("Utilities", result.Manifest.Category);
    }

    [Theory]
    [InlineData("{")][InlineData("null")][InlineData("{}")][InlineData("[]")]
    public void RejectsInvalidJson(string json) => Assert.False(new JsonPackageManifestReader().Parse(json).IsValid);

    [Theory]
    [InlineData("schemaVersion", "2")][InlineData("id", "null")][InlineData("id", "\"Bad ID\"")]
    [InlineData("architecture", "123")][InlineData("architecture", "\"unknown\"")]
    [InlineData("files", "null")][InlineData("files", "[null]")][InlineData("files", "[]")]
    [InlineData("name", "\" \"")][InlineData("category", "null")]
    [InlineData("entryPoint", "\"packages/editor/setup.msi\"")]
    [InlineData("installationMode", "\"VerifiedUnattended\"")]
    public void RejectsInvalidMetadata(string property, string value)
    {
        var node = JsonNode.Parse(ValidJson())!;
        node[property] = JsonNode.Parse(value);
        Assert.False(new JsonPackageManifestReader().Parse(node.ToJsonString()).IsValid);
    }

    [Fact]
    public void RejectsUnknownAndDuplicateProperties()
    {
        Assert.False(new JsonPackageManifestReader().Parse(ValidJson().Replace("\"schemaVersion\":1", "\"extra\":true,\"schemaVersion\":1")).IsValid);
        Assert.False(new JsonPackageManifestReader().Parse(ValidJson().Replace("\"schemaVersion\":1", "\"schemaVersion\":1,\"schemaVersion\":1")).IsValid);
    }

    [Theory]
    [InlineData("../setup.exe")][InlineData("packages/editor/../setup.exe")]
    [InlineData("C:\\setup.exe")][InlineData("/packages/editor/setup.exe")]
    [InlineData("\\\\server\\share\\setup.exe")][InlineData("packages/editor/setup.exe:stream")]
    [InlineData("packages/editor/NUL.exe")][InlineData("packages/editor/setup.exe.")]
    [InlineData("packages/editor//setup.exe")][InlineData("packages/editor/./setup.exe")]
    public void RejectsUnsafePortablePaths(string path) => Assert.Throws<ArgumentException>(() => PortablePackagePath.Normalize(path));

    [Fact]
    public void ResolvesBothSeparatorsUnderConfiguredRoot()
    {
        using var directory = new TemporaryDirectory();
        Assert.Equal(Path.Combine(directory.Root, "packages", "editor", "setup.exe"), PortablePackagePath.Resolve(directory.Root, "packages\\editor\\setup.exe"));
    }

    [Fact]
    public void RejectsBadHashAndDuplicateFilePaths()
    {
        var node = JsonNode.Parse(ValidJson())!;
        node["files"]![0]!["sha256"] = "not a hash";
        Assert.False(new JsonPackageManifestReader().Parse(node.ToJsonString()).IsValid);
        node = JsonNode.Parse(ValidJson())!;
        node["files"]!.AsArray().Add(node["files"]![0]!.DeepClone());
        Assert.False(new JsonPackageManifestReader().Parse(node.ToJsonString()).IsValid);
    }
}
