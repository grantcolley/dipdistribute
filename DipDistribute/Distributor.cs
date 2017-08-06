﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;

namespace DipDistribute
{
    public class Distributor : IDistributor
    {
        private HttpClient logClient;
        private string dependencyDirectory;

        public async Task<Step> RunAsync(Step step)
        {
            if (step == null)
            {
                throw new Exception(CreateMessage($"Step is null. Machine Name: {Environment.MachineName}"));
            }

            if (string.IsNullOrWhiteSpace(step.RunName))
            {
                throw new Exception(CreateMessage(step, "Run Name is missing."));
            }

            if (string.IsNullOrWhiteSpace(step.StepName))
            {
                throw new Exception(CreateMessage(step, "Step Name is missing."));
            }

            if (string.IsNullOrWhiteSpace(step.LogUri))
            {
                throw new Exception(CreateMessage(step, "Log Url is missing."));
            }

            logClient = new HttpClient();
            logClient.DefaultRequestHeaders.Accept.Clear();
            logClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            logClient.BaseAddress = new Uri(step.LogUri);

            return await ProcessStep(step).ConfigureAwait(false);
        }

        private async Task<Step> ProcessStep(Step step)
        {
            var initialised = await InitialiseStepAsync(step).ConfigureAwait(false);

            if (initialised)
            {
                var success = await RunStepAsync(step).ConfigureAwait(false);

                if (success)
                {
                    var completed = await CompleteStepAsync(step).ConfigureAwait(false);
                }
            }

            return step;
        }

        private async Task<bool> InitialiseStepAsync(Step step)
        {
            try
            {               
                step.Status = StepStatus.Initialise;

                Log(step);

                dependencyDirectory = Path.Combine(Directory.GetCurrentDirectory(), step.RunName);
                if (!Directory.Exists(dependencyDirectory))
                {
                    Log(step, $"Create directory {dependencyDirectory}");

                    Directory.CreateDirectory(dependencyDirectory);
                }

                if (step.Dependencies == null
                    || step.Dependencies.Length == 0)
                {
                    Log(step, "No dependencies");
                    return true;
                }

                return await DownloadDependenciesAsync(step).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log(step, ex.ToString());
                return false;
            }
        }

        private async Task<bool> DownloadDependenciesAsync(Step step)
        {
            try
            {
                Log(step, "Downloading dependencies...");

                // TODO: increase MaxResponseContentBufferSize = 1000000, the default buffer size is 65,536.
                // https://msdn.microsoft.com/en-us/library/hh696703(v=vs.110).aspx

                // Also checkout thi:
                // https://msdn.microsoft.com/en-us/library/hh556530(v=vs.110).aspx

                var client = new HttpClient() { MaxResponseContentBufferSize = 1000000 };
                client.DefaultRequestHeaders.Accept.Clear();
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                //foreach (var filePath in step.Dependencies)
                //{
                //    var uri = new Uri($"{step.DependencyUri}?file={filePath}");
                //    var stream = await client.GetStreamAsync(uri);

                //    var fileName = filePath.Split('\\');
                //    using (var file = File.Create(Path.Combine(dependencyDirectory, fileName[fileName.Length - 1])))
                //    {
                //        byte[] buffer = new byte[8 * 1024];
                //        int len;
                //        while ((len = stream.Read(buffer, 0, buffer.Length)) > 0)
                //        {
                //            file.Write(buffer, 0, len);
                //        }

                //        stream.Dispose();
                //        stream = null;
                //    }
                //}

                var dependencies = new Task<long>[step.Dependencies.Length];

                for(int i = 0; i < dependencies.Length; i++)
                {
                    dependencies[i] = DownloadDependencyAsync(client, step.DependencyUri, step.Dependencies[i]);
                }

                var downloads = new long[step.Dependencies.Length];

                for(int i = 0; i < downloads.Length; i++)
                {
                    downloads[i] = await dependencies[i];
                }

                return true;
            }
            catch (Exception ex)
            {
                Log(step, ex.ToString());
                return false;
            }
        }

        private async Task<long> DownloadDependencyAsync(HttpClient client, string dependencyUri, string filePath)
        {
            var uri = new Uri($"{dependencyUri}?file={filePath}");
            var stream = await client.GetStreamAsync(uri);
            var length = stream.Length;

            var fileName = filePath.Split('\\');
            using (var file = File.Create(Path.Combine(dependencyDirectory, fileName[fileName.Length - 1])))
            {
                byte[] buffer = new byte[8 * 1024];
                int len;
                while ((len = stream.Read(buffer, 0, buffer.Length)) > 0)
                {
                    file.Write(buffer, 0, len);
                }

                stream.Dispose();
                stream = null;
            }

            return length;
        }

        private async Task<bool> RunStepAsync(Step step)
        {
            try
            {
                step.Status = StepStatus.InProgress;

                Log(step);

                if (string.IsNullOrWhiteSpace(step.TargetAssembly))
                {
                    Log(step, "TargetAssembly is missing.");
                    return true;
                }

                if (string.IsNullOrWhiteSpace(step.TargetType))
                {
                    Log(step, "TargetType is missing.");
                    return true;
                }

                var dependencies = GetDependencyAssemblyNames(step);

                var assemblyLoader = new AssemblyLoader(dependencyDirectory, dependencies);
                var assembly = assemblyLoader.LoadFromAssemblyPath(Path.Combine(dependencyDirectory, step.TargetAssembly));

                var type = assembly.GetType(step.TargetType);
                dynamic obj = Activator.CreateInstance(type);
                var result = await obj.RunAsync(step);

                return true;
            }
            catch (Exception ex)
            {
                Log(step, ex.ToString());
                return false;
            }
        }

        private async Task<bool> CompleteStepAsync(Step step)
        {
            try
            {
                step.Status = StepStatus.Complete;

                Log(step);

                // TODO: Run transitions asynchronously...

                return true;
            }
            catch (Exception ex)
            {
                Log(step, ex.ToString());
                return false;
            }
        }

        private IList<string> GetDependencyAssemblyNames(Step step)
        {
            var dependencies = new List<string>();
            foreach (string filePath in step.Dependencies)
            {
                var filePathSplit = filePath.Split('\\');
                var fileName = filePathSplit[filePathSplit.Length - 1];
                var name = fileName.Substring(0, fileName.LastIndexOf('.'));
                dependencies.Add(name);
            }

            return dependencies;
        }

        private async void Log(Step step, string message = "")
        {
                var logMessage = CreateMessage(step, message);
                await logClient.PutAsync("api/distributor/log", new StringContent(logMessage));
        }

        private string CreateMessage(string message)
        {
            return CreateMessage(new Step(), message);
        }

        private string CreateMessage(Step step, string message)
        {
            var logMessage = $"RunId: {step.RunId}; Run Name: {step.RunName}; StepId: {step.StepId}; Step Name: {step.StepName}; Step Status: {step.Status}";

            if (!string.IsNullOrWhiteSpace(message))
            {
                logMessage += $"; Message: {message}";
            }

            return logMessage;
        }
    }
}
