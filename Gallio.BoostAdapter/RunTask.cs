using Gallio.Model;
using Gallio.Model.Filters;
using Gallio.Model.Messages;
using Gallio.Model.Messages.Execution;
using Gallio.Model.Schema;
using Gallio.Model.Tree;
using Gallio.Runtime.Logging;
using Gallio.Runtime.ProgressMonitoring;

using System;
using System.IO;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Gallio.BoostAdapter.Model
{
    /// <summary>
    /// Performs tests exploration first (switching off tests based on passed filter) and then runs enabled tests.
    /// </summary>
    internal class RunTask: BoostTestTask
    {
        private Stack<TestStep> testStepsStack_ = new Stack<TestStep>();
        private Stack<TestResult> testResultsStack_ = new Stack<TestResult>();
        private Stack<ulong> testsCountStack_ = new Stack<ulong>();
        private IProgressMonitor subMonitor_;
        private ProgressMonitorTaskCookie taskCookie_;
        private bool taskStarted_;

        protected delegate bool TestDelegate();
        protected delegate bool TestStartDelegate(uint count);
        protected delegate bool TestUnitDelegate([MarshalAs(UnmanagedType.LPWStr)] string name, bool isSuite);
        protected delegate bool TestUnitFinishedDelegate([MarshalAs(UnmanagedType.LPWStr)] string name, bool isSuite, uint elapsed);
        protected delegate bool AssertionResultDelegate(bool passed);
        protected delegate bool ExceptionCaughtDelegate([MarshalAs(UnmanagedType.LPWStr)] string what);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        delegate void BoostTestRunDelegate(
            [MarshalAs(UnmanagedType.LPWStr)] string filePath,

            VisitorDelegate testCaseVisitor,
            VisitorDelegate beginTestSuiteVisitor,
            VisitorDelegate endTestSuiteVisitor,
            VisitorDelegate shouldSkipVisitor,

            TestStartDelegate testStart,
            TestDelegate testFinish,
            TestDelegate testAborted,
            TestUnitDelegate testUnitStart,
            TestUnitFinishedDelegate testUnitFinish,
            TestUnitDelegate testUnitSkipped,
            TestUnitDelegate testUnitAborted,
            AssertionResultDelegate assertionResult,
            ExceptionCaughtDelegate exceptionCaught,

            ErrorReporterDelegate errorReporter
        );

        protected override string BridgeFunctionName
        {
            get { return "BoostTestRun"; }
        }

        protected override void Execute(IntPtr bridgeFunc, IProgressMonitor subMonitor)
        {
            subMonitor_ = subMonitor;
            taskStarted_ = false;

            BoostTestRunDelegate bridge =
                (BoostTestRunDelegate)Marshal.GetDelegateForFunctionPointer(
                    bridgeFunc,
                    typeof(BoostTestRunDelegate)
                );

            VisitorDelegate visitTestCase = new VisitorDelegate(VisitTestCase);
            VisitorDelegate beginVisitTestSuite = new VisitorDelegate(BeginVisitTestSuite);
            VisitorDelegate endVisitTestSuite = new VisitorDelegate(EndVisitTestSuite);
            VisitorDelegate shouldSkip = new VisitorDelegate(ShouldSkip);

            TestStartDelegate testStart = new TestStartDelegate(TestStart);
            TestDelegate testFinish = new TestDelegate(TestFinish);
            TestDelegate testAborted = new TestDelegate(TestAborted);
            TestUnitDelegate testUnitStart = new TestUnitDelegate(TestUnitStart);
            TestUnitFinishedDelegate testUnitFinish = new TestUnitFinishedDelegate(TestUnitFinish);
            TestUnitDelegate testUnitSkipped = new TestUnitDelegate(TestUnitSkipped);
            TestUnitDelegate testUnitAborted = new TestUnitDelegate(TestUnitAborted);
            AssertionResultDelegate assertionResult = new AssertionResultDelegate(AssertionResult);
            ExceptionCaughtDelegate exceptionCaught = new ExceptionCaughtDelegate(ExceptionCaught);

            ErrorReporterDelegate errorReporter =
                new ErrorReporterDelegate((text) => Logger.Log(LogSeverity.Error, text));

            bridge(
                File.FullName,

                visitTestCase,
                beginVisitTestSuite,
                endVisitTestSuite,
                shouldSkip,

                testStart,
                testFinish,
                testAborted,
                testUnitStart,
                testUnitFinish,
                testUnitSkipped,
                testUnitAborted,
                assertionResult,
                exceptionCaught,

                errorReporter
            );

            // Cancel all tests that were not completed, if any.
            while (testStepsStack_.Count > 0)
            {
                TestResult result = testResultsStack_.Pop();
                result.Outcome = TestOutcome.Canceled;

                MessageSink.Publish(
                    new TestStepFinishedMessage
                    {
                        StepId = testStepsStack_.Pop().Id,
                        Result = result
                    }
                );
            }

            if (taskStarted_) taskCookie_.Dispose();
        }

        private bool ShouldSkip(string name)
        {
            // Sorry, guys, but that's the most irrational filtering scheme I've ever met ...
            switch (ExecutionOptions.FilterSet.Evaluate(CurrentTest))
            {
                case FilterSetResult.Exclude:
                    return true;

                case FilterSetResult.Include:
                    return false;

                default:
                    // See if any of our parents match.
                    for (Test parent = CurrentTest.Parent; parent != null; parent = parent.Parent)
                    {
                        switch (ExecutionOptions.FilterSet.Evaluate(parent))
                        {
                            case FilterSetResult.Exclude:
                                return true;

                            case FilterSetResult.Include:
                                return false;

                            default:
                                break;
                        }
                    }

                    // If nothing is found - translate that to 'continue' for test
                    // suites (as they may have some children tests enabled) and to either
                    // 'include' for test cases (if there are no explicit inclusion rules in filter)
                    // or to 'skip' if there are.
                    return CurrentTest.IsTestCase && ExecutionOptions.FilterSet.HasInclusionRules;
            }
        }

        private bool TestStart(uint testCasesCount)
        {
            try
            {
                taskCookie_ = subMonitor_.BeginTask("Running tests in " + File.Name, testCasesCount);
                taskStarted_ = true;

                // Tests tree construction must be finished then.
                TestModelSerializer.PublishTestModel(TestModel, MessageSink);

                testStepsStack_.Push(new TestStep(TestModel.RootTest, null));
                testResultsStack_.Push(new TestResult(TestOutcome.Passed));
                testsCountStack_.Push(0);

                MessageSink.Publish(
                    new TestStepStartedMessage
                    {
                        Step = new TestStepData(testStepsStack_.Peek())
                    }
                );
            }
            catch (Exception ex)
            {
                HandleException(ex);
            }

            return !ProgressMonitor.IsCanceled;
        }

        private bool TestFinish()
        {
            try
            {
                TestResult result = testResultsStack_.Pop();

                if (testsCountStack_.Pop() == 0 && CurrentTestSuite.Children.Count > 0)
                {
                    result.Outcome = TestOutcome.Skipped;
                }

                MessageSink.Publish(
                    new TestStepFinishedMessage
                    {
                        StepId = testStepsStack_.Pop().Id,
                        Result = result
                    }
                );
            }
            catch (Exception ex)
            {
                HandleException(ex);
            }

            return !ProgressMonitor.IsCanceled;
        }

        private bool TestAborted()
        {
            try
            {
                testResultsStack_.Peek().Outcome = TestOutcome.Failed;
            }
            catch (Exception ex)
            {
                HandleException(ex);
            }

            return !ProgressMonitor.IsCanceled;
        }

        private bool TestUnitStart(string name, bool isSuite)
        {
            try
            {
                ProgressMonitor.SetStatus("Running " + name);

                Test test = TestModel.FindTest(GenerateTestId(name));
                TestStep step = new TestStep(test, testStepsStack_.Peek());
                TestResult result = new TestResult(TestOutcome.Passed);

                if (isSuite)
                {
                    CurrentTestSuite = test;
                    testsCountStack_.Push(0);
                }
                CurrentTest = test;

                step.IsTestCase = !isSuite;

                testStepsStack_.Push(step);
                testResultsStack_.Push(result);

                MessageSink.Publish(
                    new TestStepStartedMessage
                    {
                        Step = new TestStepData(step)
                    }
                );
            }
            catch (Exception ex)
            {
                HandleException(ex);
            }

            return !ProgressMonitor.IsCanceled;
        }

        private bool TestUnitFinish(string name, bool isSuite, uint elapsed)
        {
            try
            {
                TestResult result = testResultsStack_.Pop();
                result.DurationInSeconds += (double)elapsed / 1000000;

                if (isSuite)
                {
                    ulong testsCount = testsCountStack_.Pop();

                    if (testsCount == 0 && CurrentTestSuite.Children.Count > 0)
                    {
                        result.Outcome = TestOutcome.Skipped;
                    }

                    testsCountStack_.Push(testsCountStack_.Pop() + testsCount);

                    CurrentTestSuite = CurrentTestSuite.Parent;
                }
                else
                {
                    if (result.Outcome != TestOutcome.Skipped)
                    {
                        testsCountStack_.Push(testsCountStack_.Pop() + 1);
                    }
                }
                CurrentTest = CurrentTestSuite;

                MessageSink.Publish(
                    new TestStepFinishedMessage
                    {
                        StepId = testStepsStack_.Pop().Id,
                        Result = result
                    }
                );

                // Make sure we propagate information further on the stack.
                // Gallio seems to do it on its own for some data, but it's not exactly
                // clear for which one ...
                testResultsStack_.Peek().Outcome =
                    testResultsStack_.Peek().Outcome.CombineWith(result.Outcome);
                testResultsStack_.Peek().AssertCount += result.AssertCount;
                testResultsStack_.Peek().DurationInSeconds += result.DurationInSeconds;

                subMonitor_.Worked(1);
            }
            catch (Exception ex)
            {
                HandleException(ex);
            }

            return !ProgressMonitor.IsCanceled;
        }

        private bool TestUnitSkipped(string name, bool isSuite)
        {
            testResultsStack_.Peek().Outcome = TestOutcome.Skipped;
            // Unlike TestUnitAborted, TestUnitFinish is not called after
            // TestUnitSkipped, so we should do it explicitly.
            return TestUnitFinish(name, isSuite, 0);
        }

        private bool TestUnitAborted(string name, bool isSuite)
        {
            try
            {
                // TestUnitFinish will be called next, so just adjust result here
                // and let TestUnitFinish do all the rest.
                testResultsStack_.Peek().Outcome = TestOutcome.Failed;
            }
            catch (Exception ex)
            {
                HandleException(ex);
            }

            return !ProgressMonitor.IsCanceled;
        }

        private bool AssertionResult(bool passed)
        {
            try
            {
                ++testResultsStack_.Peek().AssertCount;

                if (!passed)
                {
                    testResultsStack_.Peek().Outcome = TestOutcome.Error;
                }
            }
            catch (Exception ex)
            {
                HandleException(ex);
            }

            return !ProgressMonitor.IsCanceled;
        }

        private bool ExceptionCaught(string what)
        {
            try
            {
                testResultsStack_.Peek().Outcome = TestOutcome.Error;
                Logger.Log(LogSeverity.Error, "Unexpected exception in test case: " + what);
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
