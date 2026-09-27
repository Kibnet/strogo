using System.Collections.Immutable;
using System.Reflection;
using Kernel.Core;
using Strogo.Notation;

namespace Strogo.Experiments;

public static class LivePilotProvenance
{
    private static readonly string[] SourceRoots =
    [
        "src/Kernel.Core", "src/Strogo.Notation", "src/Strogo.Experiments", "src/Strogo.ExperimentCli",
        "fixtures/e10-live-pilot", "tests/Strogo.Experiments.Conformance"
    ];

    private static readonly string[] SourceFiles =
    [
        ".gitattributes", "Directory.Build.props", "global.json", "Kernel.slnx", "NuGet.Config", "tools/Run-E10LivePilot.ps1",
        "specs/2026-09-26-e10-live-paired-pilot-v0.1.md"
    ];

    public static ImplementationIdentity CreateImplementation(string repositoryRoot, string dotNetPath, string dotNetSdkVersion, string codexNodePath, string codexEntryPath, string experimentCliAssemblyPath)
    {
        string root = Path.GetFullPath(repositoryRoot);
        var files = new List<string>();
        foreach (string relativeRoot in SourceRoots)
        {
            string directory = Path.Combine(root, relativeRoot.Replace('/', Path.DirectorySeparatorChar));
            if (!Directory.Exists(directory)) throw LivePilotJson.Failure("ImplementationSourceMissing");
            files.AddRange(Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
                .Where(path => !Path.GetRelativePath(directory, path).Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj")));
        }
        foreach (string relative in SourceFiles)
        {
            string path = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path)) throw LivePilotJson.Failure("ImplementationSourceMissing");
            files.Add(path);
        }
        var parts = files.Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(path => (Path.GetRelativePath(root, path).Replace('\\', '/'), File.ReadAllBytes(path)))
            .ToArray();
        return new(
            LivePilotJson.Digest(parts),
            DigestRequired(experimentCliAssemblyPath),
            DigestRequired(typeof(LivePilotProvenance).Assembly.Location),
            DigestRequired(typeof(NotationCompiler).Assembly.Location),
            DigestRequired(typeof(CanonicalJson).Assembly.Location),
            DigestRequired(dotNetPath),
            dotNetSdkVersion,
            DigestRequired(codexNodePath),
            DigestRequired(codexEntryPath));
    }

    public static InvocationIdentity CreateInvocation(string pilotRoot, string runId, ImplementationIdentity implementation)
    {
        string root = Path.GetFullPath(pilotRoot);
        string session = Path.Combine(root, "sessions", runId);
        string package = Path.Combine(root, "packages", runId);
        var arguments = ImmutableArray.Create(
            "exec", "--ephemeral", "--ignore-user-config", "--ignore-rules", "--skip-git-repo-check",
            "--sandbox", "read-only", "--output-schema", Path.Combine(package, "response.schema.json"), "--json",
            "--output-last-message", Path.Combine(root, "runs", runId, "response.json"),
            "--model", LivePilotIdentity.RequestedModel, "--cd", session,
            "-c", "project_doc_max_bytes=0", "-c", "shell_environment_policy.inherit=none",
            "-c", $"model_reasoning_effort=\"{LivePilotIdentity.RequestedReasoning}\"", "-");
        var environment = ImmutableArray.Create("APPDATA", "CODEX_HOME", "ComSpec", "LOCALAPPDATA", "PATH", "SystemRoot", "TEMP", "TMP", "USERPROFILE");
        var skeleton = new InvocationIdentity(
            implementation.CodexNodeDigest,
            implementation.CodexEntryDigest,
            arguments,
            session,
            environment,
            Path.Combine(package, "prompt.txt"),
            string.Empty);
        return skeleton with { Digest = CanonicalJson.RawDigest(CanonicalJson.Encode(skeleton)) };
    }

    public static void VerifyInvocation(InvocationIdentity identity, string pilotRoot, string runId, ImplementationIdentity implementation)
    {
        var expected = CreateInvocation(pilotRoot, runId, implementation);
        if (!CanonicalJson.Encode(identity).AsSpan().SequenceEqual(CanonicalJson.Encode(expected))) throw LivePilotJson.Failure("InvocationIdentityMismatch");
    }

    public static void VerifyImplementation(ImplementationIdentity actual, ImplementationIdentity expected)
    {
        if (!CanonicalJson.Encode(actual).AsSpan().SequenceEqual(CanonicalJson.Encode(expected)))
            throw LivePilotJson.Failure("ImplementationIdentityMismatch");
    }

    public static bool GitStatusIsClean(string porcelainOutput) => string.IsNullOrWhiteSpace(porcelainOutput);

    public static bool EvidenceRootIsOutsideRepository(string repositoryRoot, string evidenceRoot)
    {
        string repository = Path.GetFullPath(repositoryRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string evidence = Path.GetFullPath(evidenceRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return !evidence.Equals(repository, StringComparison.OrdinalIgnoreCase)
            && !evidence.StartsWith(repository + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static string DigestRequired(string path)
    {
        string full = Path.GetFullPath(path);
        if (!File.Exists(full)) throw LivePilotJson.Failure("ImplementationBinaryMissing");
        return CanonicalJson.RawDigest(File.ReadAllBytes(full));
    }
}
