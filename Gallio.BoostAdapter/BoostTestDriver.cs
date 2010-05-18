// Copyright 2010 Alexander Tsvyashchenko - http://www.ndl.kiev.ua
// 
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
// 
//     http://www.apache.org/licenses/LICENSE-2.0
// 
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using Gallio.BoostAdapter.Utils;
using Gallio.Common.Messaging;
using Gallio.Common.Reflection;
using Gallio.Model;
using Gallio.Model.Isolation;
using Gallio.Runtime.Hosting;
using Gallio.Runtime.Logging;
using Gallio.Runtime.ProgressMonitoring;

using System;
using System.IO;
using System.Collections.Generic;

namespace Gallio.BoostAdapter.Model
{
    public class BoostTestDriver: BaseTestDriver
    {
        private ILogger logger_;

        public BoostTestDriver(ILogger logger)
        {
            logger_ = logger;
        }
    
        protected override bool IsTestImpl(IReflectionPolicy reflectionPolicy, ICodeElementInfo codeElement)
        {
            logger_.Log(LogSeverity.Warning, "BoostTestDriver::IsTestImpl - not implemented!");
            return base.IsTestImpl(reflectionPolicy, codeElement);
        }
    
        protected override bool IsTestPartImpl(IReflectionPolicy reflectionPolicy, ICodeElementInfo codeElement)
        {
            logger_.Log(LogSeverity.Warning, "BoostTestDriver::IsTestPartImpl - not implemented!");
            return base.IsTestPartImpl(reflectionPolicy, codeElement);
        }
    
        protected override void DescribeImpl(
            IReflectionPolicy reflectionPolicy,
            IList<ICodeElementInfo> codeElements,
            TestExplorationOptions testExplorationOptions,
            IMessageSink messageSink,
            IProgressMonitor progressMonitor
        )
        {
            logger_.Log(LogSeverity.Warning, "BoostTestDriver::DescribeImpl - not implemented!");
            base.DescribeImpl(reflectionPolicy, codeElements, testExplorationOptions, messageSink, progressMonitor);
        }
    
        protected override void ExploreImpl(
            ITestIsolationContext testIsolationContext,
            TestPackage testPackage,
            TestExplorationOptions testExplorationOptions,
            IMessageSink messageSink,
            IProgressMonitor progressMonitor
        )
        {
            ExecuteTask<ExploreTask>(
                testIsolationContext,
                testPackage,
                null,
                messageSink,
                progressMonitor,
                "Exploring tests."
            );
        }
    
        protected override void RunImpl(
            ITestIsolationContext testIsolationContext,
            TestPackage testPackage,
            TestExplorationOptions testExplorationOptions,
            TestExecutionOptions testExecutionOptions,
            IMessageSink messageSink,
            IProgressMonitor progressMonitor
        )
        {
            if (testExecutionOptions.SkipTestExecution)
                return;

            ExecuteTask<RunTask>(
                testIsolationContext,
                testPackage,
                testExecutionOptions,
                messageSink,
                progressMonitor,
                "Running tests."
            );
        }

        private void ExecuteTask<Task>(
            ITestIsolationContext testIsolationContext,
            TestPackage testPackage,
            TestExecutionOptions testExecutionOptions,
            IMessageSink messageSink,
            IProgressMonitor progressMonitor,
            string status
        ) where Task: IsolatedTask, new()
        {
            using (progressMonitor.BeginTask(status, Math.Max(testPackage.Files.Count, 1)))
            {
                RemoteMessageSink remoteSink = new RemoteMessageSink(messageSink);
                RemoteLogger remoteLogger = new RemoteLogger(logger_);

                foreach (FileInfo file in testPackage.Files)
                {
                    if (progressMonitor.IsCanceled)
                        return;

                    // Parse PE header/import table.
                    DllInfo info = DllParser.GetDllInfo(file.FullName);
                    string configType = null;

                    // Search for Boost.Test framework DLL in imports and determine whether Debug
                    // or Release configuration is used by parsing DLL name.
                    // Theoretically this is not 100% correct - there might be multiple references
                    // to different Boost.Test frameworks DLLs and/or their names might not follow
                    // boost naming conventions, but it's hard to imagine practical case for that.
                    if (info.ImportedDlls != null)
                    {
                        foreach (string DllName in info.ImportedDlls)
                        {
                            if (DllName.StartsWith(
                                    "boost_unit_test_framework",
                                    StringComparison.InvariantCultureIgnoreCase))
                            {
                                if (DllName.IndexOf("-gd-", StringComparison.InvariantCultureIgnoreCase) != -1)
                                {
                                    configType = "Debug";
                                }
                                else
                                {
                                    configType = "Release";
                                }

                                break;
                            }
                        }
                    }

                    // This is not Boost.Test-testable DLL, skip it.
                    // Show this as warning, because we should have determine that on
                    // test driver filtering process.
                    if (String.IsNullOrEmpty(configType))
                    {
                        logger_.Log(
                            LogSeverity.Warning,
                            String.Format("The file {0} is not Boost.Test-testable DLL, skipping it.", file.FullName)
                        );
                        progressMonitor.Worked(1);
                        continue;
                    }

                    using (IProgressMonitor remoteMonitor =
                        new RemoteProgressMonitor(progressMonitor.CreateSubProgressMonitor(1)))
                    {
                        // TODO: Gallio seems to not propagate canceled event from remote monitor
                        // back to original one???
                        progressMonitor.Canceled += (sender, e) => { remoteMonitor.Cancel(); };

                        HostSetup hostSetup = testPackage.CreateHostSetup();
                        // Tell Gallio to use appropriate version of framework when setting up
                        // host process so that we can later load proper version of native DLL.
                        hostSetup.ProcessorArchitecture = info.Architecture;

                        testIsolationContext.RunIsolatedTask<Task>(
                            hostSetup,
                            (statusMessage) => progressMonitor.SetStatus(statusMessage),
                            new object[]
                            {
                                file,
                                info.Architecture.ToString(),
                                configType,
                                testExecutionOptions,
                                remoteLogger,
                                remoteMonitor,
                                remoteSink
                            }
                        );
                    }
                }
            }
        }
    }
}
//------------------------------------------------------------------------------
