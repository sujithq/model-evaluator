using ModelEvaluator.Core.Evaluation;
using ModelEvaluator.Core.Results;
using ModelEvaluator.Core.Scenarios;

namespace ModelEvaluator.Core.Tests;

public sealed class InstructionAdherenceCheckerTests : IDisposable
{
    private const string GlobalJson =
        """
        {
          "sdk": {
            "version": "10.0.100"
          }
        }
        """;

    private readonly string _root = Path.Combine(Path.GetTempPath(), "eval-tests", Guid.NewGuid().ToString("N"));
    private readonly InstructionAdherenceChecker _checker = new();

    public InstructionAdherenceCheckerTests()
    {
        Directory.CreateDirectory(ScenarioDirectory);
        Write(Path.Combine(ScenarioDirectory, "starter", "global.json"), GlobalJson);
        Write(Path.Combine(ScenarioDirectory, "starter", "BENCHMARK.md"), "do not modify");
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string ScenarioDirectory => Path.Combine(_root, "scenario");

    private string Workspace => Path.Combine(_root, "workspace");

    [Fact]
    public void CompliantWorkspace_PassesEveryCheck()
    {
        CreateCompliantWorkspace();

        var results = _checker.Check(Workspace, CreatePackage());

        Assert.All(results, result => Assert.Equal(CheckStatus.Passed, result.Status));
    }

    [Fact]
    public void MissingLayoutAndReadme_AreReported()
    {
        CreateCompliantWorkspace();
        File.Delete(Path.Combine(Workspace, "README.md"));
        Directory.Delete(Path.Combine(Workspace, "tests"), recursive: true);

        var results = _checker.Check(Workspace, CreatePackage());

        Assert.Equal(CheckStatus.Failed, Find(results, "instructions.readme").Status);
        Assert.Equal(CheckStatus.Failed, Find(results, "instructions.tests-layout").Status);
    }

    [Fact]
    public void WrongTargetFramework_IsReported()
    {
        CreateCompliantWorkspace();
        Write(
            Path.Combine(Workspace, "src", "App", "App.csproj"),
            Project("net8.0", "enable"));

        var results = _checker.Check(Workspace, CreatePackage());

        Assert.Equal(CheckStatus.Failed, Find(results, "instructions.target-framework").Status);
        Assert.Contains("net8.0", Find(results, "instructions.target-framework").Details, StringComparison.Ordinal);
    }

    [Fact]
    public void DisabledNullable_IsReported()
    {
        CreateCompliantWorkspace();
        Write(Path.Combine(Workspace, "src", "App", "App.csproj"), Project("net10.0", "disable"));

        var results = _checker.Check(Workspace, CreatePackage());

        Assert.Equal(CheckStatus.Failed, Find(results, "instructions.nullable-enabled").Status);
    }

    [Fact]
    public void NullableInheritedFromDirectoryBuildProps_IsAccepted()
    {
        CreateCompliantWorkspace();
        Write(Path.Combine(Workspace, "src", "App", "App.csproj"),
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
            </Project>
            """);
        Write(Path.Combine(Workspace, "Directory.Build.props"),
            """
            <Project>
              <PropertyGroup>
                <Nullable>enable</Nullable>
              </PropertyGroup>
            </Project>
            """);

        var results = _checker.Check(Workspace, CreatePackage());

        Assert.Equal(CheckStatus.Passed, Find(results, "instructions.nullable-enabled").Status);
    }

    [Fact]
    public void DisallowedPackage_IsReported()
    {
        CreateCompliantWorkspace();
        Write(Path.Combine(Workspace, "src", "App", "App.csproj"),
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <Nullable>enable</Nullable>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="Newtonsoft.Json" Version="13.0.3" />
              </ItemGroup>
            </Project>
            """);

        var results = _checker.Check(Workspace, CreatePackage());

        var check = Find(results, "instructions.allowed-dependencies");
        Assert.Equal(CheckStatus.Failed, check.Status);
        Assert.Contains("Newtonsoft.Json", check.Details, StringComparison.Ordinal);
    }

    [Fact]
    public void ModifiedSuppliedFiles_AreReported()
    {
        CreateCompliantWorkspace();
        Write(Path.Combine(Workspace, "BENCHMARK.md"), "edited by the model");

        var results = _checker.Check(Workspace, CreatePackage());

        var check = Find(results, "instructions.preserved-files");
        Assert.Equal(CheckStatus.Failed, check.Status);
        Assert.Contains("BENCHMARK.md", check.Details, StringComparison.Ordinal);
    }

    [Fact]
    public void UnpinnedSdk_IsReported()
    {
        CreateCompliantWorkspace();
        Write(Path.Combine(Workspace, "global.json"), """{ "sdk": { "version": "8.0.100" } }""");

        var results = _checker.Check(Workspace, CreatePackage());

        Assert.Equal(CheckStatus.Failed, Find(results, "instructions.sdk-pinned").Status);
    }

    [Fact]
    public void MissingRequiredGlob_IsReported()
    {
        CreateCompliantWorkspace();
        File.Delete(Path.Combine(Workspace, "App.sln"));

        var results = _checker.Check(Workspace, CreatePackage());

        Assert.Equal(CheckStatus.Failed, Find(results, "instructions.required-path:*.sln").Status);
        Assert.Equal(CheckStatus.Failed, Find(results, "instructions.solution-file").Status);
    }

    private static CheckResult Find(IEnumerable<CheckResult> results, string id) =>
        results.Single(r => r.Id == id);

    private ScenarioPackage CreatePackage() => new(
        new ScenarioDefinition
        {
            Id = "test-scenario",
            Name = "Test scenario",
            ProjectType = "Console",
            BenchmarkVersion = "1.0.0",
            AcceptanceProject = "acceptance/Test.csproj",
            AllowedPackages = ["xunit", "Microsoft.NET.Test.Sdk"],
            RequiredGlobs = ["*.sln", "src/**/*.csproj"],
            PreservedPaths = ["global.json", "BENCHMARK.md"],
        },
        ScenarioDirectory,
        "prompt");

    private void CreateCompliantWorkspace()
    {
        Directory.CreateDirectory(Workspace);
        Write(Path.Combine(Workspace, "global.json"), GlobalJson);
        Write(Path.Combine(Workspace, "BENCHMARK.md"), "do not modify");
        Write(Path.Combine(Workspace, "README.md"), "# App");
        Write(Path.Combine(Workspace, "App.sln"), "Microsoft Visual Studio Solution File");
        Write(Path.Combine(Workspace, "src", "App", "App.csproj"), Project("net10.0", "enable"));
        Write(Path.Combine(Workspace, "src", "App", "Program.cs"), "// app");
        Write(
            Path.Combine(Workspace, "tests", "App.Tests", "App.Tests.csproj"),
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <Nullable>enable</Nullable>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="xunit" Version="2.9.3" />
                <PackageReference Include="Microsoft.NET.Test.Sdk" Version="18.10.1" />
              </ItemGroup>
            </Project>
            """);
    }

    private static string Project(string targetFramework, string nullable) =>
        $"""
         <Project Sdk="Microsoft.NET.Sdk">
           <PropertyGroup>
             <TargetFramework>{targetFramework}</TargetFramework>
             <Nullable>{nullable}</Nullable>
           </PropertyGroup>
         </Project>
         """;

    private static void Write(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }
}
