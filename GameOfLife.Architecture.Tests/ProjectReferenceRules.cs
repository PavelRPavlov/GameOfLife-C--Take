using System.Xml.Linq;

namespace GameOfLife.Architecture.Tests;

/// <summary>
/// Infrastructure tests that guard the solution's project-reference graph.
///
/// Each project is only permitted to reference the projects listed in
/// <see cref="AllowedReferences"/>. The moment someone adds a
/// <c>&lt;ProjectReference&gt;</c> that is not on a project's allow-list, the
/// corresponding test turns red — even before the new edge causes any runtime
/// or architectural damage.
///
/// The headline rule: the Blazor <c>WebClient</c> must never reference the
/// <c>Api</c> directly. <c>Shared</c> is the contract seam between them.
/// </summary>
public sealed class ProjectReferenceRules
{
    /// <summary>
    /// The complete, intended reference graph of the solution.
    ///
    /// Every project in the solution MUST appear as a key here — a project with
    /// no permitted references maps to an empty set. This is deliberate: adding a
    /// brand-new project to the solution without classifying it here fails
    /// <see cref="Every_project_is_classified"/>, forcing a conscious decision
    /// about what that project is allowed to depend on.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string[]> AllowedReferences =
        new Dictionary<string, string[]>
        {
            // Foundational projects depend on nothing else in the solution.
            ["GameOfLife.Core"] = [],
            ["GameOfLife.Shared"] = [],

            // Hosts depend on the domain (Core) and the wire contracts (Shared),
            // but NEVER on each other. WebClient reaching for Api is the exact
            // violation these tests exist to catch.
            ["GameOfLife.Api"] = ["GameOfLife.Core", "GameOfLife.Shared"],
            ["GameOfLife.WebClient"] = ["GameOfLife.Core", "GameOfLife.Shared"],

            // Test projects reference their subject (plus Core where they build
            // domain fixtures directly).
            ["GameOfLife.Core.Tests"] = ["GameOfLife.Core"],
            ["GameOfLife.Api.Tests"] = ["GameOfLife.Api"],
            ["GameOfLife.WebClient.Tests"] = ["GameOfLife.WebClient", "GameOfLife.Core"],

            // This project inspects the graph and must stay dependency-free.
            ["GameOfLife.Architecture.Tests"] = [],
        };

    public static TheoryData<string> AllProjectFiles()
    {
        var data = new TheoryData<string>();
        foreach (var projectFile in ProjectFiles())
            data.Add(Path.GetFileNameWithoutExtension(projectFile));
        return data;
    }

    /// <summary>
    /// Every project may only reference projects on its explicit allow-list.
    /// Adding a disallowed <c>&lt;ProjectReference&gt;</c> fails this test.
    /// </summary>
    [Theory]
    [MemberData(nameof(AllProjectFiles))]
    public void Project_only_references_allowed_projects(string projectName)
    {
        Assert.True(
            AllowedReferences.TryGetValue(projectName, out var allowed),
            $"'{projectName}' is not classified in {nameof(AllowedReferences)}. " +
            "Add it to the reference map and declare what it is allowed to depend on.");

        var actual = ReferencedProjectNames(projectName);
        var disallowed = actual.Except(allowed!).OrderBy(x => x).ToArray();

        Assert.True(
            disallowed.Length == 0,
            $"'{projectName}' references projects it is not allowed to: " +
            $"[{string.Join(", ", disallowed)}]. " +
            $"Allowed: [{string.Join(", ", allowed!)}]. " +
            "If this new reference is intentional, update the reference map.");
    }

    /// <summary>
    /// Explicit, self-documenting guard for the most important invariant:
    /// the web client must talk to the server only through shared contracts,
    /// never by referencing the API project directly.
    /// </summary>
    [Fact]
    public void WebClient_must_not_reference_Api()
    {
        var references = ReferencedProjectNames("GameOfLife.WebClient");

        Assert.DoesNotContain("GameOfLife.Api", references);
    }

    /// <summary>
    /// Guards against silent drift: every project discovered on disk must be
    /// accounted for in the reference map, and vice versa.
    /// </summary>
    [Fact]
    public void Every_project_is_classified()
    {
        var onDisk = ProjectFiles()
            .Select(Path.GetFileNameWithoutExtension)
            .OrderBy(x => x)
            .ToArray();

        var classified = AllowedReferences.Keys.OrderBy(x => x).ToArray();

        Assert.Equal(classified, onDisk);
    }

    private static string[] ReferencedProjectNames(string projectName)
    {
        var projectFile = ProjectFiles()
            .Single(p => Path.GetFileNameWithoutExtension(p) == projectName);

        var doc = XDocument.Load(projectFile);

        // .csproj files here use the SDK-style default (no XML namespace),
        // so element names match without a namespace qualifier.
        return doc.Descendants("ProjectReference")
            .Select(e => (string?)e.Attribute("Include"))
            .Where(include => !string.IsNullOrWhiteSpace(include))
            .Select(include => Path.GetFileNameWithoutExtension(include!.Replace('\\', Path.DirectorySeparatorChar)))
            .OrderBy(x => x)
            .ToArray();
    }

    private static IEnumerable<string> ProjectFiles() =>
        Directory.EnumerateFiles(SolutionRoot(), "*.csproj", SearchOption.AllDirectories);

    private static string SolutionRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null && !dir.EnumerateFiles("*.sln").Any())
            dir = dir.Parent;

        Assert.True(dir is not null, "Could not locate the solution root (no .sln found walking up from the test output directory).");
        return dir!.FullName;
    }
}
