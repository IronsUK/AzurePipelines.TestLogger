using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using SampleUnitTestProject;

namespace AzurePipelines.TestLogger.Tests
{
    [TestFixture]
    public class IntegrationTests
    {
        private string _vsTestExeFilePath;
        private string _sampleUnitTestProjectDllFilePath;
        private string _azurePipelinesTestLoggerAssemblyPath;

        [OneTimeSetUp]
        public void SetUpFixture()
        {
            _vsTestExeFilePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "Microsoft Visual Studio",
                "2022",
                "Enterprise",
                "Common7",
                "IDE",
                "CommonExtensions",
                "Microsoft",
                "TestWindow",
                "vstest.console.exe");

            string configuration = "Debug";

#if RELEASE
            configuration = "Release";
#endif

            string rootRepositoryPath = GetRootRepositoryPath();
            _sampleUnitTestProjectDllFilePath = Path.Combine(rootRepositoryPath, "tests", "SampleUnitTestProject", "bin", configuration, "net8.0", "SampleUnitTestProject.dll");
            _azurePipelinesTestLoggerAssemblyPath = Path.Combine(rootRepositoryPath, "src", "AzurePipelines.TestLogger", "bin", configuration, "net8.0");
        }

        [Test]
        public void ExecuteTestWithInvalidAzureDevopsCollectionUriContinuesTestExecution()
        {
            // Given
            string fullyQualifiedTestMethodName = GetFullyQualifiedTestMethodName(
                typeof(UnitTest1),
                nameof(UnitTest1.TestMethod));

            const string collectionUri = "collectionUri";

            // When
            int exitCode = ExecuteUnitTestWithLogger(
                testMethod: fullyQualifiedTestMethodName,
                collectionUri: collectionUri);

            // Then
            Assert.AreEqual(0, exitCode);
        }

        [Test]
        public async Task ExecuteTestWithDataTestMethodLogsEachDataRow()
        {
            // Given
            string fullyQualifiedTestMethodName = GetFullyQualifiedTestMethodName(
                typeof(UnitTest1),
                nameof(UnitTest1.DataTestMethod));

            // When
            TestResults testResults = await StartServerAndExecuteUnitTestWithLoggerAsync(
                fullyQualifiedTestMethodName);

            // Then
            Assert.AreEqual(0, testResults.ExitCode);
            Assert.AreEqual(2, testResults.CapturedRequests.Count);
        }

        [Test]
        public async Task ExecuteEndToEndTest()
        {
            // Given
            string fullyQualifiedTestMethodName = GetFullyQualifiedTestMethodName(
                typeof(UnitTest1),
                nameof(UnitTest1.TestMethod));

            // int exitCode = ExecuteUnitTestWithLogger(
            //        testMethod: fullyQualifiedTestMethodName,
            //        collectionUri: "https://dev.azure.com/wtw-bda-outsourcing-product/",
            //        buildId: "192852",
            //        teamProject: "BenefitConnect",
            //        buildRequestedFor: "PW UNIT TEST",
            //        agentName: "No AGENT",
            //        agentJobName: "Job 1");

            ApiClientFactory apiClientFactory = new ApiClientFactory();
            IApiClient apiClient = apiClientFactory.CreateWithDefaultCredentials("https://dev.azure.com/wtw-bda-outsourcing-product/", "BenefitConnect", "5.0");
            object result = await apiClient.GetRunsByBuildId(192852, CancellationToken.None);

            Assert.IsNotNull(result);
        }

        private async Task<TestResults> StartServerAndExecuteUnitTestWithLoggerAsync(
            string fullyQualifiedTestMethodName)
        {
            // Create the Server
            IRequestStore requestStore = new RequestStore();

            IWebHost host = WebHost.CreateDefaultBuilder()
                .UseKestrel()
                .ConfigureServices(configureServices =>
                {
                    configureServices.AddSingleton(requestStore);
                })
                .UseUrls("http://127.0.0.1:0") // listen on a random available port
                .UseStartup<MockAzureDevOpsTestRunLogCollectorServer>()
                .Build();

            await host.StartAsync();

            try
            {
                // Get the server's listening address
                IServerAddressesFeature serverAddresses = host.Services
                    .GetRequiredService<IServer>()
                    .Features.Get<IServerAddressesFeature>();

                string serverUrl = serverAddresses.Addresses.First();

                Console.WriteLine($"Server is listening on: {serverUrl}");

                int exitCode = ExecuteUnitTestWithLogger(
                    testMethod: fullyQualifiedTestMethodName,
                    collectionUri: $"{serverUrl}/");

                List<HttpRequest> capturedRequests = (List<HttpRequest>)requestStore;

                return new TestResults
                {
                    ExitCode = exitCode,
                    CapturedRequests = capturedRequests,
                };
            }
            finally
            {
                await host.StopAsync();
            }
        }

        private class TestResults
        {
            public int ExitCode { get; set; }
            public List<HttpRequest> CapturedRequests { get; set; }
        }

        private static string GetFullyQualifiedTestMethodName(Type type, string methodName)
        {
            MethodInfo methodInfo = type.GetMethod(methodName);
            return $"{type.Namespace}.{type.Name}.{methodInfo.Name}";
        }

        private int ExecuteUnitTestWithLogger(
            bool verbose = true,
            bool useDefaultCredentials = true,
            string apiVersion = "3.0-preview.2",
            bool groupTestResultsByClassName = false,
            string testMethod = "SampleUnitTestProject.UnitTest1.TestMethod1",
            string collectionUri = "collectionUri",
            string teamProject = "teamProject",
            string buildId = "buildId",
            string buildRequestedFor = "buildRequestedFor",
            string agentName = "agentName",
            string agentJobName = "jobName")
        {
            List<string> loggerArguments = new List<string>
            {
                "AzurePipelines",
                $"Verbose={verbose}",
                $"UseDefaultCredentials={useDefaultCredentials}",
                $"ApiVersion={apiVersion}",
                $"GroupTestResultsByClassName={groupTestResultsByClassName}"
            };

            List<string> arguments = new List<string>
            {
                "test",
                $"\"{_sampleUnitTestProjectDllFilePath}\"",
                $"--filter \"FullyQualifiedName={testMethod}\"",
                $"--logger \"{string.Join(";", loggerArguments)}\"",
                $"--test-adapter-path \"{_azurePipelinesTestLoggerAssemblyPath}\""
            };

            Dictionary<string, string> environmentVariables = new Dictionary<string, string>
            {
                { EnvironmentVariableNames.TeamFoundationCollectionUri, collectionUri },
                { EnvironmentVariableNames.TeamProject, teamProject },
                { EnvironmentVariableNames.BuildId, buildId },
                { EnvironmentVariableNames.BuildRequestedFor, buildRequestedFor },
                { EnvironmentVariableNames.AgentName, agentName },
                { EnvironmentVariableNames.AgentJobName, agentJobName },
            };

            ProcessRunner processRunner = new ProcessRunner();
            return processRunner.Run("dotnet", arguments, environmentVariables);
        }

        private static string GetRootRepositoryPath()
        {
            string currentDirectory = Directory.GetCurrentDirectory();
            string fileNameToFind = "root";

            // Start from the current directory and move up the directory tree
            while (!File.Exists(Path.Combine(currentDirectory, fileNameToFind)))
            {
                string parentDirectory = Directory.GetParent(currentDirectory)?.FullName;
                if (parentDirectory == null || parentDirectory == currentDirectory)
                {
                    throw new Exception($"Failed to find file '{fileNameToFind}' in the directory tree.");
                }

                currentDirectory = parentDirectory;
            }

            return currentDirectory;
        }
    }
}
