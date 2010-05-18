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
