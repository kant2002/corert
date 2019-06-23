﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;



namespace Build.Tasks
{
    public class ComputeManagedAssembliesToCompileToNative : DesktopCompatibleTask
    {
        [Required]
        public ITaskItem[] Assemblies
        {
            get;
            set;
        }

        /// <summary>
        /// The CoreRT-specific System.Private.* assemblies that must be used instead of the netcoreapp2.1 versions.
        /// </summary>
        [Required]
        public ITaskItem[] SdkAssemblies
        {
            get;
            set;
        }

        /// <summary>
        /// The set of AOT-specific framework assemblies we currently need to use which will replace the same-named ones
        /// in the app's closure.
        /// </summary>
        [Required]
        public ITaskItem[] FrameworkAssemblies
        {
            get;
            set;
        }

        /// <summary>
        /// The native apphost (whose name ends up colliding with the CoreRT output binary) 
        /// </summary>
        [Required]
        public string DotNetAppHostExecutableName
        {
            get;
            set;
        }

        /// <summary>
        /// The CoreCLR dotnet host fixer library that can be skipped during publish
        /// </summary>
        [Required]
        public string DotNetHostFxrLibraryName
        {
            get;
            set;
        }

        /// <summary>
        /// The CoreCLR dotnet host policy library that can be skipped during publish
        /// </summary>
        [Required]
        public string DotNetHostPolicyLibraryName
        {
            get;
            set;
        }

        /// <summary>
        /// Ready-to-run images targets CoreCLR; use its framework instead of the CoreRT private one
        /// </summary>
        public string CompilationMode
        {
            get;
            set;
        }

        [Output]
        public ITaskItem[] ManagedAssemblies
        {
            get;
            set;
        }

        [Output]
        public ITaskItem[] AssembliesToSkipPublish
        {
            get;
            set;
        }

        public override bool Execute()
        {
            var list = new List<ITaskItem>();
            var assembliesToSkipPublish = new List<ITaskItem>();
            var coreRTFrameworkAssembliesToUse = new HashSet<string>();
            bool readyToRunCompilation = CompilationMode?.Equals("readytorun", StringComparison.OrdinalIgnoreCase) ?? false;

            foreach (ITaskItem taskItem in SdkAssemblies)
            {
                coreRTFrameworkAssembliesToUse.Add(Path.GetFileName(taskItem.ItemSpec));
            }

            foreach (ITaskItem taskItem in FrameworkAssemblies)
            {
                coreRTFrameworkAssembliesToUse.Add(Path.GetFileName(taskItem.ItemSpec));
            }

            foreach (ITaskItem taskItem in Assemblies)
            {
                // In the case of disk-based assemblies, this holds the file path
                string itemSpec = taskItem.ItemSpec;

                if (!readyToRunCompilation)
                {
                    // Skip the native apphost (whose name ends up colliding with the CoreRT output binary) and supporting libraries
                    if (itemSpec.EndsWith(DotNetAppHostExecutableName, StringComparison.OrdinalIgnoreCase) || itemSpec.Contains(DotNetHostFxrLibraryName) || itemSpec.Contains(DotNetHostPolicyLibraryName))
                    {
                        assembliesToSkipPublish.Add(taskItem);
                        continue;
                    }

                    // Prototype aid - remove the native CoreCLR runtime pieces from the publish folder
                    if (itemSpec.Contains("microsoft.netcore.app") && (itemSpec.Contains("\\native\\") || itemSpec.Contains("/native/")))
                    {
                        assembliesToSkipPublish.Add(taskItem);
                        continue;
                    }

                    // Remove any assemblies whose implementation we want to come from CoreRT's package.
                    // Currently that's System.Private.* SDK assemblies and a bunch of framework assemblies.
                    if (coreRTFrameworkAssembliesToUse.Contains(Path.GetFileName(itemSpec)))
                    {
                        assembliesToSkipPublish.Add(taskItem);
                        continue;
                    }
                }

                try
                {
                    using (FileStream moduleStream = File.OpenRead(itemSpec))
                    using (var module = new PEReader(moduleStream))
                    {
                        if (module.HasMetadata)
                        {
                            MetadataReader moduleMetadataReader = module.GetMetadataReader();
                            if (moduleMetadataReader.IsAssembly)
                            {
                                if (readyToRunCompilation)
                                {
                                    // Skip publish of the IL assembly since the ready-to-run binary will be published after compilation
                                    assembliesToSkipPublish.Add(taskItem);
                                    list.Add(taskItem);
                                }
                                else
                                {
                                    string culture = moduleMetadataReader.GetString(moduleMetadataReader.GetAssemblyDefinition().Culture);

                                    if (culture == "" || culture.Equals("neutral", StringComparison.OrdinalIgnoreCase))
                                    {
                                        // CoreRT doesn't consume resource assemblies yet so skip them
                                        assembliesToSkipPublish.Add(taskItem);
                                        list.Add(taskItem);
                                    }
                                }
                            }
                        }
                    }
                }
                catch (BadImageFormatException)
                {
                }
            }

            ManagedAssemblies = list.ToArray();
            AssembliesToSkipPublish = assembliesToSkipPublish.ToArray();

            return true;
        }
    }
}