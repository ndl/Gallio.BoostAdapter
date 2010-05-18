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

using Gallio.Common.Messaging;
using Gallio.Model;
using Gallio.Model.Isolation;
using Gallio.Model.Tree;
using Gallio.Runtime.Logging;
using Gallio.Runtime.ProgressMonitoring;

using System;
using System.IO;
using System.ComponentModel;
using System.Reflection;
using System.Runtime.InteropServices;

namespace Gallio.BoostAdapter.Model
{
    /// <summary>
    /// Base class for tasks performed using native bridge DLL to Boost.Test.
    /// </summary>
    internal abstract class BoostTestTask: IsolatedTask
    {
        /// <summary>
        /// The name of native bridge DLL to use - the DLL is expected to be located in subfolder,
        /// see suffix generation below.
        /// </summary>
        /// <remarks>
        /// This task is executed in isolated context, so we assume we're already running on appropriate
        /// architecture and can load native bridge DLL with required architecture without problems.
        /// </remarks>
        private const string NativeDllName = "Gallio.BoostAdapter.Native.dll";

        private FileInfo file_;
        private string architecture_, configuration_;
        private TestExecutionOptions testExecutionOptions_;
        private ILogger logger_;
        private IProgressMonitor progressMonitor_;
        private IMessageSink messageSink_;
        private TestModel testModel_;
        private Test currentTest_, currentTestSuite_;

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetDllDirectory(string lpPathName);

        [DllImport("kernel32", SetLastError = true)]
        private static extern IntPtr LoadLibrary(string lpFileName);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool FreeLibrary(IntPtr hModule);

        [DllImport("kernel32", CharSet = CharSet.Ansi, ExactSpelling = true, SetLastError = true)]
        private static extern IntPtr GetProcAddress(IntPtr hModule, string procName);

        /// <summary>
        /// Delegate type for function called from native bridge DLL during test tree traversal.
        /// </summary>
        protected delegate bool VisitorDelegate([MarshalAs(UnmanagedType.LPWStr)] string name);

        /// <summary>
        /// Delegate type for function called from native bridge DLL to report errors.
        /// </summary>
        protected delegate void ErrorReporterDelegate([MarshalAs(UnmanagedType.LPWStr)] string text);

        /// <summary>
        /// Information about DLL file to be tested.
        /// </summary>
        protected FileInfo File { get { return file_; } }

        protected TestExecutionOptions ExecutionOptions { get { return testExecutionOptions_; } }

        protected ILogger Logger { get { return logger_; } }

        protected IProgressMonitor ProgressMonitor { get { return progressMonitor_; } }

        protected IMessageSink MessageSink { get { return messageSink_; } }

        /// <summary>
        /// Test model representing explored tests.
        /// </summary>
        /// <remarks>
        /// Gallio design requires to fill it both for "Explore" and "Run" calls, hence
        /// all functionality/data structures required for exploration are placed into base
        /// task.
        /// </remarks>
        protected TestModel TestModel { get { return testModel_; } }

        /// <summary>
        /// Test suite that is currently being explored or run.
        /// </summary>
        protected Test CurrentTestSuite
        {
            get { return currentTestSuite_; }
            set { currentTestSuite_ = value; }
        }

        /// <summary>
        /// Test that is currently being explored or run.
        /// </summary>
        protected Test CurrentTest
        {
            get { return currentTest_; }
            set { currentTest_ = value; }
        }
        
        /// <summary>
        /// Must be overriden by derived classes to return function name we should
        /// retrieve from native bridge DLL to perform the task.
        /// </summary>
        protected abstract string BridgeFunctionName { get; }

        /// <summary>
        /// Must be overriden by derived classes to perform actual task execution.
        /// </summary>
        protected abstract void Execute(IntPtr bridgeFunc, IProgressMonitor subMonitor);

        /// <summary>
        /// Called automatically when isolation task is ready to run.
        /// </summary>
        protected override object RunImpl(object[] args)
        {
            file_ = (FileInfo)args[0];
            architecture_ = (string)args[1];
            configuration_ = (string)args[2];
            testExecutionOptions_ = (TestExecutionOptions)args[3];
            logger_ = (ILogger)args[4];
            progressMonitor_ = (IProgressMonitor)args[5];

            using (QueuedMessageSink sink = new QueuedMessageSink((IMessageSink)args[6]))
            {
                messageSink_ = sink;
                testModel_ = new TestModel();
                currentTest_ = testModel_.RootTest;
                currentTestSuite_ = testModel_.RootTest;

                using (progressMonitor_.BeginTask("Processing " + file_.Name, 100))
                {
                    // Expect native DLL to be reachable in subdirectory relative to the current assembly path.
                    string assemblyDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                    string suffix = Path.Combine(architecture_, configuration_);

                    // Make sure we can find the right version of Boost.Test DLL.
                    if (!SetDllDirectory(Path.Combine(assemblyDir, suffix)))
                    {
                        Logger.Log(
                            LogSeverity.Error,
                            String.Format(
                                "Failed to adjust DLL directory search path: {0}",
                                new Win32Exception(Marshal.GetLastWin32Error()).Message
                            )
                        );
                        return null;
                    }

                    // Try loading native bridge DLL.
                    IntPtr hModule = LoadLibrary(Path.Combine(assemblyDir, Path.Combine(suffix, NativeDllName)));

                    if (hModule == IntPtr.Zero)
                    {
                        Logger.Log(
                            LogSeverity.Error,
                            String.Format(
                                "Failed to load native DLL to communicate with Boost.Test: {0}",
                                new Win32Exception(Marshal.GetLastWin32Error()).Message
                            )
                        );
                        return null;
                    }

                    try
                    {
                        // Make sure we allow loading additional DLLs
                        // from the same folder our testable DLL is located in.
                        if (!SetDllDirectory(File.DirectoryName))
                        {
                            Logger.Log(
                                LogSeverity.Error,
                                String.Format(
                                    "Failed to adjust DLL directory search path: {0}",
                                    new Win32Exception(Marshal.GetLastWin32Error()).Message
                                )
                            );
                            return null;
                        }

                        progressMonitor_.Worked(14);

                        // Retrieve pointer to function in native bridge DLL that is required to
                        // perform our task.
                        IntPtr bridgeFunc = GetProcAddress(hModule, BridgeFunctionName);

                        if (bridgeFunc == IntPtr.Zero)
                        {
                            Logger.Log(
                                LogSeverity.Error,
                                String.Format(
                                    "Failed to retrieve entry point {0} in Boost.Test interface: {1}",
                                    BridgeFunctionName,
                                    new Win32Exception(Marshal.GetLastWin32Error()).Message
                                )
                            );
                            return null;
                        }

                        progressMonitor_.Worked(1);

                        // Perform the task.
                        Execute(bridgeFunc, progressMonitor_.CreateSubProgressMonitor(80));
                    }
                    finally
                    {
                        FreeLibrary(hModule);
                        progressMonitor_.Worked(5);
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Generates unique test ID by file name and path to test.
        /// </summary>
        protected string GenerateTestId(string name)
        {
            return File.FullName + ": " + currentTestSuite_.FullName + "/" + name;
        }

        protected void HandleException(Exception ex)
        {
            Logger.Log(LogSeverity.Error, "Unexpected exception while running tests.", ex);
            ProgressMonitor.Cancel();
        }

        protected bool VisitTestCase(string name)
        {
            try
            {
                Test test = new Test(name, null);
                test.Id = GenerateTestId(name);
                test.Kind = TestKinds.Test;
                test.IsTestCase = true;
                currentTest_ = test;
                currentTestSuite_.AddChild(test);
            }
            catch (Exception ex)
            {
                HandleException(ex);
            }

            return !ProgressMonitor.IsCanceled;
        }

        protected bool BeginVisitTestSuite(string name)
        {
            try
            {
                Test suite = new Test(name, null);
                suite.IsTestCase = false;
                suite.Id = GenerateTestId(name);

                // If we're at top level - adjust test suite appearance and add metadata.
                if (currentTestSuite_ == testModel_.RootTest)
                {
                    string configString = String.Format("{0} ({1})", architecture_, configuration_);
                    suite.Kind = "Boost.Test Dll";
                    suite.Name = String.Format("{0} - {1}", name, configString);
                    suite.Metadata.SetValue(MetadataKeys.File, File.FullName);
                    suite.Metadata.SetValue(MetadataKeys.Configuration, configString);
                }
                else
                {
                    suite.Kind = TestKinds.Suite;
                }

                currentTest_ = suite;
                currentTestSuite_.AddChild(suite);
                currentTestSuite_ = suite;
            }
            catch (Exception ex)
            {
                HandleException(ex);
            }

            return !ProgressMonitor.IsCanceled;
        }

        protected bool EndVisitTestSuite(string name)
        {
            try
            {
                currentTestSuite_ = currentTestSuite_.Parent;
                currentTest_ = currentTestSuite_;
            }
            catch (Exception ex)
            {
                HandleException(ex);
            }

            return !ProgressMonitor.IsCanceled;
        }
    }
}
//------------------------------------------------------------------------------
