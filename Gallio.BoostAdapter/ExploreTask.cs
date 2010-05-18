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

using Gallio.Model.Messages;
using Gallio.Runtime.Logging;
using Gallio.Runtime.ProgressMonitoring;

using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Gallio.BoostAdapter.Model
{
    /// <summary>
    /// Traverses tests tree in testable DLL, accumulates all tests in TestModel and
    /// then publishes it to Gallio.
    /// </summary>
    internal class ExploreTask: BoostTestTask
    {
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        delegate void BoostTestExploreDelegate(
            [MarshalAs(UnmanagedType.LPWStr)] string filePath,

            VisitorDelegate testCaseVisitor,
            VisitorDelegate beginTestSuiteVisitor,
            VisitorDelegate endTestSuiteVisitor,

            ErrorReporterDelegate errorReporter
        );

        protected override string BridgeFunctionName
        {
            get { return "BoostTestExplore"; }
        }

        protected override void Execute(IntPtr bridgeFunc, IProgressMonitor subMonitor)
        {
            using (subMonitor.BeginTask("Exploring " + File.Name, 100))
            {
                BoostTestExploreDelegate bridge =
                    (BoostTestExploreDelegate)Marshal.GetDelegateForFunctionPointer(
                        bridgeFunc,
                        typeof(BoostTestExploreDelegate)
                    );

                VisitorDelegate visitTestCase = new VisitorDelegate(VisitTestCase);
                VisitorDelegate beginVisitTestSuite = new VisitorDelegate(BeginVisitTestSuite);
                VisitorDelegate endVisitTestSuite = new VisitorDelegate(EndVisitTestSuite);

                ErrorReporterDelegate errorReporter =
                    new ErrorReporterDelegate((text) => Logger.Log(LogSeverity.Error, text));

                bridge(
                    File.FullName,

                    visitTestCase,
                    beginVisitTestSuite,
                    endVisitTestSuite,

                    errorReporter
                );

                TestModelSerializer.PublishTestModel(TestModel, MessageSink);
            }
        }
    }
}
//------------------------------------------------------------------------------
