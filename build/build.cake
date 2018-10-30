///////////////////////////////////////////////////////////////////////////////
// TOOLS AND ADDIN
///////////////////////////////////////////////////////////////////////////////
#tool "nuget:?package=xunit.runner.console"

#tool nuget:?package=gitlink
#addin nuget:?package=Cake.Git

// codecov.io
#tool nuget:?package=Codecov
#addin nuget:?package=Cake.Codecov


///////////////////////////////////////////////////////////////////////////////
// ARGUMENTS
///////////////////////////////////////////////////////////////////////////////

var target = Argument("target", "Default");
var configuration = Argument("configuration", "Debug");

///////////////////////////////////////////////////////////////////////////////
// GLOBAL VARS
///////////////////////////////////////////////////////////////////////////////
var solution = File("../ClrSpy.sln");
var lastCommit = GitLogTip("../");

///////////////////////////////////////////////////////////////////////////////
// SETUP / TEARDOWN
///////////////////////////////////////////////////////////////////////////////

Setup(ctx =>
{
    Information("WorkingDirectory = " + Environment.CurrentDirectory);

    Environment.SetEnvironmentVariable("DOTNET_CLI_TELEMETRY_OPTOUT", "1");
    Environment.SetEnvironmentVariable("DOTNET_SKIP_FIRST_TIME_EXPERIENCE", "1");
    Environment.SetEnvironmentVariable("DOTNET_CLI_UI_LANGUAGE", "en");
    Information(@"Last commit {0}
    Short message: {1}
    Author:        {2}
    Authored:      {3:yyyy-MM-dd HH:mm:ss}
    Committer:     {4}
    Committed:     {5:yyyy-MM-dd HH:mm:ss}",
    lastCommit.Sha,
    lastCommit.MessageShort,
    lastCommit.Author.Name,
    lastCommit.Author.When,
    lastCommit.Committer.Name,
    lastCommit.Committer.When
    );
    Information("Running tasks...");
});

Teardown(ctx =>
{
    Information("Finished running tasks.");
});

///////////////////////////////////////////////////////////////////////////////
// TASKS
///////////////////////////////////////////////////////////////////////////////

Task("Clean").Does(() =>
{
    CleanDirectories("../artifacts");
    DotNetCoreClean(solution, new DotNetCoreCleanSettings() { Verbosity = DotNetCoreVerbosity.Minimal,});
});

Task("Restore").Does(() =>
{
    DotNetCoreRestore("../");
});

Task("Rebuild")
    .IsDependentOn("Clean")
    .IsDependentOn("Restore")
    .IsDependentOn("Build");

Task("Build").Does(() => {
    var msbuildSettings = new DotNetCoreMSBuildSettings()
    {
        NoLogo = true,
    };
    msbuildSettings.Properties.Add("FULL_PDB",  new List<string>(){"true"});
    DotNetCoreBuild(solution,  new DotNetCoreBuildSettings()
    {
        Configuration = configuration,
        Verbosity = DotNetCoreVerbosity.Minimal,
        MSBuildSettings = msbuildSettings,
        // vs code problemMatcher workaround
        ArgumentCustomization = args => args.Append("/p:GenerateFullPaths=true"),
    });
});
Task("RunTests").Does(() => {
    var tests = GetFiles("../tests/**/*.Tests.csproj");
    foreach(var test in tests)
    {
        Information($"Start tests on project {test.GetFilenameWithoutExtension()}");


        DotNetCoreTest(test.FullPath, new DotNetCoreTestSettings()
        {
            Configuration = configuration,
            Verbosity = DotNetCoreVerbosity.Minimal,
            NoBuild = true,
            NoRestore = true,
            ResultsDirectory = $"./../../artifacts/tests/reports/",
            // dotnet test has only TRX logger, we add xunit xml logger from XunitXml.TestLogger package
            // https://github.com/xunit/xunit/issues/1154
            // can't use full path because https://github.com/spekt/xunit.testlogger/pull/4
            Logger = $"xunit;LogFilePath=./../../artifacts/tests/reports/{test.GetFilenameWithoutExtension()}.xml",
            TestAdapterPath = test.GetDirectory(),
            //DiagnosticOutput = false,
            //DiagnosticFile = $"./../../artifacts/tests/reports/{test.GetFilenameWithoutExtension()}_diag.xml",
        });
    }

});

Task("Coverage").Does(() => {
    var tests = GetFiles("../tests/**/*.Tests.csproj");
    foreach(var test in tests)
    {
        Information($"Start tests with coverlet on project {test.GetFilenameWithoutExtension()}");

        var coverageFile = test.GetFilenameWithoutExtension() + ".opencover.xml";
        var coveragePath = Directory("../artifacts/coverage/");
        DotNetCoreTest(test.FullPath, new DotNetCoreTestSettings()
        {
            Configuration = "Debug",
            Verbosity = DotNetCoreVerbosity.Minimal,
            NoBuild = false,
            NoRestore = false,
            ResultsDirectory = $"./../../artifacts/tests/reports/",
            // dotnet test has only TRX logger, we add xunit xml logger from XunitXml.TestLogger package
            // https://github.com/xunit/xunit/issues/1154
            // can't use full path because https://github.com/spekt/xunit.testlogger/pull/4
            Logger = $"xunit;LogFilePath=./../../artifacts/tests/reports/{test.GetFilenameWithoutExtension()}.xml",
            TestAdapterPath = test.GetDirectory(),
            // Don't forget add
            // <PackageReference Include="coverlet.msbuild" />
            // to csproj with tests
            // Cake.Coverlet has bugs, sometimes throw null reference exception
            // Use plain https://github.com/tonerdo/coverlet
            ArgumentCustomization = args => args.Append("/p:CollectCoverage=true")
                                                .Append("/p:CoverletOutputFormat=opencover")
                                                .AppendSwitchQuoted("/p:CoverletOutput","=", System.IO.Path.Combine(MakeAbsolute(coveragePath).FullPath, coverageFile))
                                                .Append("/p:ExcludeByFile=\"**/AssemblyProperties.cs\""),
            //DiagnosticOutput = false,
            //DiagnosticFile = $"./../../artifacts/tests/reports/{test.GetFilenameWithoutExtension()}_diag.xml",

        });

        Codecov(System.IO.Path.Combine(coveragePath, coverageFile), "38faf842-b413-4657-9cff-f4ebed8904f4");
    }
});

Task("Default").IsDependentOn("Rebuild");

RunTarget(target);