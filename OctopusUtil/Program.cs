using System;
using System.Configuration;
using System.Linq;
using Octopus.Client;
using Octopus.Client.Model;

namespace OctopusUtil
{
    class Program
    {
        private static string server;
        private static string apiKey;

        static void Main(string[] args)
        {
            if (args == null || args.Length == 0)
            {
                Console.WriteLine("Arguments missing");
                return;
            }
            server = ConfigurationManager.AppSettings.Get("api.host");
            if (string.IsNullOrEmpty(server))
            {
                Console.WriteLine("api.host setting is missing");
                return;
            }
            apiKey = ConfigurationManager.AppSettings.Get("api.key");
            if (string.IsNullOrEmpty(apiKey))
            {
                Console.WriteLine("api.key setting is missing");
                return;
            }

            if (args[0].Equals("/create-group-release"))
            {
                if (args.Length < 3)
                {
                    Console.WriteLine("Arguments required: groupName* version* environmentName");
                    return;
                }
                var groupName = args[1];
                var version = args[2];
                var environmentName = args.Length >= 4 ? args[3] : null;
                CreateGroupRelease(groupName, version, environmentName);
            }
        }

        private static void CreateGroupRelease(string groupName, string version, string environmentName)
        {
            Console.WriteLine($"Creating releases version {version} for group '{groupName}'...");
            var client = new OctopusClient(new OctopusServerEndpoint(server, apiKey));
            var repo = new OctopusRepository(client);

            var group = repo.ProjectGroups.FindByName(groupName);
            if (group == null) throw new NullReferenceException($"Group '{groupName}' not found.");

            var projects = repo.Projects.FindMany(x => x.ProjectGroupId == group.Id && !x.IsDisabled);
            Console.WriteLine($"Found {projects.Count} projects...");

            var environment = environmentName != null ? repo.Environments.FindByName(environmentName) : null;

            foreach (var project in projects)
            {
                var latestRelease = repo.Projects.GetReleases(project).Items
                    .OrderByDescending(r => SemanticVersion.Parse(r.Version))
                    .FirstOrDefault();

                if (latestRelease == null || latestRelease.Version != version)
                {
                    latestRelease = CreateRelease(repo, project, version);
                }
                else
                {
                    Console.WriteLine($"\t'{project.Name}' with version {version} is up to date");
                }

                if (environment != null && latestRelease != null && repo.Deployments.FindMany(x => x.ReleaseId == latestRelease.Id).Count == 0)
                {
                    CreateDeploy(repo, project, environment, latestRelease);
                }
            }
        }

        private static void CreateDeploy(IOctopusRepository repo, ProjectResource project, EnvironmentResource environment, ReleaseSummaryResource latestRelease)
        {
            Console.WriteLine($"\t # deploying '{project.Name}' version {latestRelease.Version} to {environment.Name}");
            var deploy = new DeploymentResource
            {
                ProjectId = project.Id,
                ReleaseId = latestRelease.Id,
                EnvironmentId = environment.Id
            };
            repo.Deployments.Create(deploy);
        }

        private static ReleaseResource CreateRelease(OctopusRepository repo, ProjectResource project, string version)
        {
            Console.WriteLine($"\t # create release for '{project.Name}' version {version}");

            var process = repo.DeploymentProcesses.Get(project.DeploymentProcessId);
            var template = repo.DeploymentProcesses.GetTemplate(process, null);

            var release = new ReleaseResource
            {
                Version = version,
                ProjectId = project.Id
            };

            foreach (var package in template.Packages)
            {
                release.SelectedPackages.Add(new SelectedPackage(package.StepName, version));
            }
            return repo.Releases.Create(release);
        }
    }
}
