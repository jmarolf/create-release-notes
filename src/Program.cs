
using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;

if (args.Length is not 5)
{
    Console.Error.WriteLine($"ERROR: Expected 5 arguments but got {args.Length}");
    return 1;
}

if (args[0] != "-prev" || args[2] != "-curr")
{
    Console.Error.WriteLine("ERROR: Invalid arguments. Invoke `release -prev {sha} -curr {sha}`");
    return 1;
}

var previousCommitSha = args[1];
var currentCommitSha = args[3];
var repositoryLocation = args[4];

return CreateReleaseNotes(previousCommitSha, currentCommitSha, repositoryLocation);

static int CreateReleaseNotes(string previousCommitSha, string currentCommitSha, string? repositoryLocation)
{
    var regex = new Regex(@"sha:(.*) name:(.*) email:(.*) title:'(.*)'");
    var isMergePRCommit = new Regex(@"^Merge pull request #(\d+) from");
    repositoryLocation ??= Environment.CurrentDirectory;
    using var gitLog = new Process();
    var prs = new List<CommitInfo>();
    gitLog.StartInfo.FileName = "git";
    gitLog.StartInfo.Arguments = $"log {previousCommitSha}..{currentCommitSha} --pretty=format:\"sha:%h name:%an email:%ae title:'%s'\"";
    gitLog.StartInfo.RedirectStandardOutput = true;
    gitLog.StartInfo.UseShellExecute = false;
    gitLog.StartInfo.CreateNoWindow = true;
    gitLog.StartInfo.WorkingDirectory = repositoryLocation;
    gitLog.OutputDataReceived += (sender, e) =>
    {
        if (e.Data is not null)
        {
            var groups = regex.Match(e.Data).Groups;
            var sha = groups[1].Value.Trim();
            var name = groups[2].Value.Trim();
            var email = groups[3].Value.Trim();
            var title = groups[4].Value.Trim();
            if (name == "dotnet-automerge-bot" || name == "dotnet bot")
            {
                return;
            }

            if (isMergePRCommit.IsMatch(title))
            {
                prs.Add(new CommitInfo(sha, name, email, title));
            }
        }
    };
    gitLog.Start();
    gitLog.BeginOutputReadLine();
    gitLog.WaitForExit();

    var infra = new List<PullRequestInfo>();
    var bugfixes = new List<PullRequestInfo>();
    var features = new List<PullRequestInfo>();
    foreach (var pr in prs)
    {
        var prNumber = isMergePRCommit.Match(pr.Title).Groups[1].Value;
        using var ghCli = new Process();
        ghCli.StartInfo.FileName = "gh";
        ghCli.StartInfo.Arguments = $"pr view {prNumber} --json \"title,body,files\"";
        ghCli.StartInfo.RedirectStandardOutput = true;
        ghCli.StartInfo.UseShellExecute = false;
        ghCli.StartInfo.CreateNoWindow = true;
        ghCli.StartInfo.WorkingDirectory = repositoryLocation;
        ghCli.OutputDataReceived += (sender, e) =>
        {
            if (e.Data is not null && JsonSerializer.Deserialize<PullRequestInfo>(e.Data) is { } prInfo)
            {
                if (prInfo.body.Contains("This is an automatically generated pull request from"))
                {
                    infra.Add(prInfo);
                    Console.WriteLine($"infra PR #{prNumber}");
                }

                // files updated are only under eng/ or /**/.*TestUtilities /**/.*UnitTests
                else if (prInfo.files.All(f => f.path.StartsWith("eng/") || f.path.Contains("TestUtilities") || f.path.Contains("UnitTests")))
                {
                    infra.Add(prInfo);
                    Console.WriteLine($"infra PR #{prNumber}");
                }

                // if body or title contains "fixes"
                else if (prInfo.body.Contains("fixes") | prInfo.title.Contains("fix"))
                {
                    bugfixes.Add(prInfo);
                    Console.WriteLine($"bugfix PR #{prNumber}");
                }

                // if body or title contains "feature"
                else if (prInfo.body.Contains("feature") || prInfo.title.Contains("feature"))
                {
                    features.Add(prInfo);
                    Console.WriteLine($"feature PR #{prNumber}");
                }
            }
        };
        ghCli.Start();
        ghCli.BeginOutputReadLine();
        ghCli.WaitForExit();
    }

    Console.WriteLine($"infrastructure PRs: {infra.Count}");
    Console.WriteLine($"bugfix PRs: {bugfixes.Count}");
    Console.WriteLine($"feature PRs: {features.Count}");


    foreach (var pr in features)
    {
        Console.WriteLine(pr.title);
    }

    return 0;
}

record CommitInfo(string SHA, string CommitterName, string CommitterEmail, string Title);

record PullRequestInfo(string title, string body, FileChange[] files);

record FileChange(string path, int additions, int deletions);