﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Nito.AsyncEx;
using NuGet.Common;
using NuGet.Packaging.Core;
using NuGet.Protocol.Core.Types;
using OpenMod.NuGet;

namespace OpenMod.Bootstrapper
{
    // NOTE TO DEVELOPER: THIS CLASS SHOULD NOT REFERENCE *ANY* OTHER OPENMOD CODE!
    // OpenMod is not loaded at this stage (except for OpenMod.NuGet and OpenMod.Common).
    /// <summary>
    /// This class is responsible for downloading OpenMod.Core, OpenMod.API, the host package and their dependencies. <br/>
    /// After download, it will boot OpenMod and then initialize the IRuntime implementation.
    /// </summary>
    public class OpenModDynamicBootstrapper
    {
        /// <summary>
        /// Bootstrap OpenMod.
        /// </summary>
        /// <param name="packageManager">The optional package manager.</param>
        /// <param name="openModFolder">The OpenMod working directory.</param>
        /// <param name="commandLineArgs">The command line arguments.</param>
        /// <param name="packageId">The ID of the host package.</param>
        /// <param name="allowPrereleaseVersions">If true, will download pre-release versions.</param>
        /// <param name="logger">The optional logger.</param>
        /// <returns>The OpenMod runtime.</returns>
        [ItemNotNull]
        public Task<object> BootstrapAsync(
            NuGetPackageManager packageManager,
            string openModFolder,
            string[] commandLineArgs,
            string packageId,
            bool allowPrereleaseVersions = false,
            ILogger logger = null)
        {
            return BootstrapAsync(packageManager, openModFolder, commandLineArgs, new List<string> { packageId }, Enumerable.Empty<string>(), allowPrereleaseVersions, logger);
        }

        /// <summary>
        /// Bootstrap OpenMod.
        /// </summary>
        /// <param name="packageManager">The optional package manager.</param>
        /// <param name="openModFolder">The OpenMod working directory.</param>
        /// <param name="commandLineArgs">The command line arguments.</param>
        /// <param name="packageIds">The IDs of the host package.</param>
        /// <param name="allowPrereleaseVersions">If true, will download pre-release versions.</param>
        /// <param name="logger">The optional logger.</param>
        /// <returns>The OpenMod runtime.</returns>
        [NotNull]
        public object Bootstrap(
            NuGetPackageManager packageManager,
            string openModFolder,
            string[] commandLineArgs,
            IEnumerable<string> packageIds,
            IEnumerable<string> ignoredDependencies,
            bool allowPrereleaseVersions = false,
            ILogger logger = null)
        {
            return AsyncContext.Run(() => BootstrapAsync(packageManager, openModFolder, commandLineArgs, packageIds, ignoredDependencies, allowPrereleaseVersions, logger));
        }

        /// <summary>
        /// Bootstrap OpenMod.
        /// </summary>
        /// <param name="packageManager">The optional package manager.</param>
        /// <param name="openModFolder">The OpenMod working directory.</param>
        /// <param name="commandLineArgs">The command line arguments.</param>
        /// <param name="packageIds">The IDs of the host package.</param>
        /// <param name="allowPrereleaseVersions">If true, will download pre-release versions.</param>
        /// <param name="ignoredDependencies">Ignored dependencies.</param>
        /// <param name="logger">The optional logger.</param>
        /// <returns>The OpenMod runtime.</returns>
        public async Task<object> BootstrapAsync(
            NuGetPackageManager packageManager,
            string openModFolder,
            string[] commandLineArgs,
            IEnumerable<string> packageIds,
            IEnumerable<string> ignoredDependencies,
            bool allowPrereleaseVersions = false,
            ILogger logger = null)
        {
            var shouldAutoUpdate = commandLineArgs.Any(arg => arg.Equals("-OpenModAutoUpdate", StringComparison.InvariantCultureIgnoreCase));
            if (!shouldAutoUpdate)
            {
                var autoUpdateValue = Environment.GetEnvironmentVariable("OpenMod_EnableAutoUpdate");
                if (string.IsNullOrEmpty(autoUpdateValue) || !bool.TryParse(autoUpdateValue, out shouldAutoUpdate))
                {
                    shouldAutoUpdate = false;
                }
            }

            logger ??= new NuGetConsoleLogger();
            openModFolder = Path.GetFullPath(openModFolder);

            if (packageManager == null)
            {
                var packagesDirectory = Path.Combine(openModFolder, "packages");
                if (!Directory.Exists(packagesDirectory))
                {
                    Directory.CreateDirectory(packagesDirectory);
                }

                packageManager = new NuGetPackageManager(packagesDirectory) { Logger = logger };
                Environment.SetEnvironmentVariable("NUGET_COMMON_APPLICATION_DATA", Path.GetFullPath(packagesDirectory));
            }

            var hostAssemblies = new List<Assembly>();
            foreach (var packageId in packageIds)
            {
                var packageIdentity = await packageManager.GetLatestPackageIdentityAsync(packageId);
                var shouldInstallOrUpdate = packageIdentity == null || shouldAutoUpdate;

                if (shouldInstallOrUpdate)
                {
                    var latestOpenModPackage = await packageManager.QueryPackageExactAsync(packageId, version: null, allowPrereleaseVersions);
                    var latestPackageIdentity = latestOpenModPackage.Identity;

                    if (packageIdentity == null || packageIdentity.Version < latestPackageIdentity.Version)
                    {
                        logger.LogInformation($"Downloading {latestPackageIdentity.Id} v{latestPackageIdentity.Version} via NuGet");
                        var installResult = await packageManager.InstallAsync(latestPackageIdentity, allowPrereleaseVersions);
                        if (installResult.Code == NuGetInstallCode.Success || installResult.Code == NuGetInstallCode.NoUpdatesFound)
                        {
                            packageIdentity = installResult.Identity;
                            logger.LogInformation(installResult.Code == NuGetInstallCode.Success
                                ? $"Finished downloading \"{packageId}\"."
                                : $"Latest \"{packageId}\" is already installed.");
                        }
                        else
                        {
                            logger.LogError($"Downloading has failed for {latestPackageIdentity.Id} v{latestPackageIdentity.Version.OriginalVersion}: {installResult.Code}");
                            if (packageIdentity == null)
                            {
                                return null;
                            }
                        }
                    }
                }

                logger.LogInformation($"Loading {packageIdentity.Id} v{packageIdentity.Version}");
                var packageAssemblies = await LoadPackageAsync(packageManager, packageIdentity);
                hostAssemblies.AddRange(packageAssemblies);
            }

            return await InitializeRuntimeAsync(packageManager, hostAssemblies, openModFolder, commandLineArgs);
        }

        private Task<IEnumerable<Assembly>> LoadPackageAsync(NuGetPackageManager packageManager, PackageIdentity identity)
        {
            var pkg = packageManager.GetNugetPackageFile(identity);
            return packageManager.LoadAssembliesFromNuGetPackageAsync(pkg);
        }

        private async Task<object> InitializeRuntimeAsync(NuGetPackageManager packageManager, List<Assembly> hostAssemblies, string workingDirectory, string[] commandlineArgs)
        {
            var runtimeAssembly = FindAssemblyInCurrentDomain("OpenMod.Runtime");
            var apiAssembly = FindAssemblyInCurrentDomain("OpenMod.API");
            var runtimeType = runtimeAssembly.GetType("OpenMod.Runtime.Runtime");
            var runtime = Activator.CreateInstance(runtimeType);

            var parametersType = apiAssembly.GetType("OpenMod.API.RuntimeInitParameters");
            var parameters = Activator.CreateInstance(parametersType);
            SetParameter(parameters, "WorkingDirectory", workingDirectory);
            SetParameter(parameters, "CommandlineArgs", commandlineArgs);
            SetParameter(parameters, "PackageManager", packageManager);

            var initMethod = runtimeType.GetMethod("InitAsync", BindingFlags.Instance | BindingFlags.Public);
            await (Task)initMethod.Invoke(runtime, new[] { hostAssemblies, parameters, null /* hostBuilderFunc */});
            return runtime;
        }

        private Assembly FindAssemblyInCurrentDomain(string name)
        {
            return AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(d => d.GetName().Name.Equals(name)) ??
                   throw new Exception($"Failed to find assembly: {name}");
        }

        private void SetParameter(object parametersObject, string name, object value)
        {
            var field = parametersObject.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public);
            field?.SetValue(parametersObject, value);
        }
    }
}
