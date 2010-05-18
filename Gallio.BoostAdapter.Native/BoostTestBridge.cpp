#include "stdafx.h"

#include <boost/test/unit_test.hpp>
#include <boost/test/execution_monitor.hpp>

#include <string>
#include <windows.h>

//------------------------------------------------------------------------------
typedef bool (__stdcall *Visitor)(const wchar_t *name);
typedef bool (__stdcall *TestEventHandler)();
typedef bool (__stdcall *TestStartEventHandler)(unsigned long count);
typedef bool (__stdcall *TestUnitEventHandler)(const wchar_t *name, bool suite);
typedef bool (__stdcall *TestUnitFinishedEventHandler)(const wchar_t *name, bool suite, unsigned long elapsed);
typedef bool (__stdcall *AssertionResultEventHandler)(bool passed);
typedef bool (__stdcall *ExceptionCaughtEventHandler)(const wchar_t *name);
typedef bool (__stdcall *ErrorReporter)(const wchar_t *text);
typedef bool (*InitFunc)();
//------------------------------------------------------------------------------
struct execution_cancelled {};
//------------------------------------------------------------------------------
namespace
{
    std::wstring GetErrorString()
    {
        LPWSTR msg = NULL;
    
        FormatMessage(
            FORMAT_MESSAGE_ALLOCATE_BUFFER | FORMAT_MESSAGE_FROM_SYSTEM | FORMAT_MESSAGE_IGNORE_INSERTS,
            NULL, 
            GetLastError(),
            MAKELANGID(LANG_NEUTRAL, SUBLANG_DEFAULT),
            (LPWSTR)&msg, 
            0,
            NULL
        );
    
        
        if(msg)
        {
            std::wstring res(msg);
            LocalFree(msg);
            return res;
        }
        else
        {
            return std::wstring();
        }
    }

    std::wstring ToWide(const std::string &str)
    {
        return std::wstring(str.begin(), str.end());
    }

    std::string GetWhat(const boost::execution_exception &ex)
    {
        if (!ex.what().is_empty())
        {
            return std::string(ex.what().begin(), ex.what().end());
        }
        else
            return "empty error string";
    }

    void CleanUp(HMODULE h)
    {
        try
        {
            boost::unit_test::framework::clear();
        }
        catch (...)
        {
        }

        if (h) FreeLibrary(h);
    }
}
//------------------------------------------------------------------------------
InitFunc FindInitFunc(HMODULE &h, const wchar_t *fileName, ErrorReporter errorReporter)
{
    h = LoadLibrary(fileName);

    if (!h)
    {
        errorReporter((std::wstring(L"Failed to load DLL ") + fileName + L": " + GetErrorString()).c_str());
        return NULL;
    }
    
    static const std::string initFuncName("init_unit_test");
    
    InitFunc initFunc = reinterpret_cast<InitFunc>(GetProcAddress(h, initFuncName.c_str()));

    // Do not report error: it might be that DLL is just not Boost.Test-enabled.
    if (!initFunc)
    {
        FreeLibrary(h);
        h = NULL;
    }

    return initFunc;
}
//------------------------------------------------------------------------------
class TestsVisitor: public boost::unit_test::test_tree_visitor
{
public:
    TestsVisitor(
        Visitor testCaseVisitor,
        Visitor beginTestSuiteVisitor,
        Visitor endTestSuiteVisitor,
        Visitor shouldSkipVisitor
    ):
        testCaseVisitor_(testCaseVisitor),
        beginTestSuiteVisitor_(beginTestSuiteVisitor),
        endTestSuiteVisitor_(endTestSuiteVisitor),
        shouldSkipVisitor_(shouldSkipVisitor)
    {
    }

    virtual void visit(const boost::unit_test::test_case &tc)
    {
        std::wstring name = ToWide(tc.p_name.get());

        if (!(*testCaseVisitor_)(name.c_str()))
            throw execution_cancelled();

        if (shouldSkipVisitor_ && (*shouldSkipVisitor_)(name.c_str()))
            tc.p_enabled.set(false);
    }

    virtual bool test_suite_start(const boost::unit_test::test_suite &ts)
    {
        std::wstring name = ToWide(ts.p_name.get());

        if (!(*beginTestSuiteVisitor_)(name.c_str()))
            throw execution_cancelled();

        if (shouldSkipVisitor_ && (*shouldSkipVisitor_)(name.c_str()))
            ts.p_enabled.set(false);

        return true;
    }

    virtual void test_suite_finish(const boost::unit_test::test_suite &ts)
    {
        if (!(*endTestSuiteVisitor_)(ToWide(ts.p_name.get()).c_str()))
            throw execution_cancelled();
    }

private:
    Visitor testCaseVisitor_, beginTestSuiteVisitor_, endTestSuiteVisitor_, shouldSkipVisitor_;
};
//------------------------------------------------------------------------------
extern "C" __declspec(dllexport) void BoostTestExplore(
    const wchar_t* fileName,
    Visitor testCaseVisitor,
    Visitor beginTestSuiteVisitor,
    Visitor endTestSuiteVisitor,
    ErrorReporter errorReporter
)
{
    HINSTANCE h = NULL;

    try
    {
        InitFunc initFunc = NULL;

        if (!(initFunc = FindInitFunc(h, fileName, errorReporter)))
            return;
    
        char *argv = "";
        boost::unit_test::framework::init(initFunc, 0, &argv);
    
        TestsVisitor visitor(testCaseVisitor, beginTestSuiteVisitor, endTestSuiteVisitor, NULL);
    
        try
        {
            boost::unit_test::traverse_test_tree(
                boost::unit_test::framework::master_test_suite(),
                visitor
            );
        }
        catch (const execution_cancelled&)
        {
        }

        boost::unit_test::framework::clear();
        FreeLibrary(h);
    }
    catch (const std::exception &ex)
    {
        errorReporter(ToWide("Boost.Test error: " + std::string(ex.what())).c_str());
        CleanUp(h);
    }
    catch (const boost::execution_exception &ex)
    {
        errorReporter(ToWide("Boost.Test error: " + GetWhat(ex)).c_str());
        CleanUp(h);
    }
    catch (...)
    {
        errorReporter(L"Boost.Test framework internal error: unknown reason");
        CleanUp(h);
    }
}
//------------------------------------------------------------------------------
class TestObserver: public boost::unit_test::test_observer
{
public:
    TestObserver(
        TestStartEventHandler testStart,
        TestEventHandler testFinish,
        TestEventHandler testAborted,
        TestUnitEventHandler testUnitStart,
        TestUnitFinishedEventHandler testUnitFinish,
        TestUnitEventHandler testUnitSkipped,
        TestUnitEventHandler testUnitAborted,
        AssertionResultEventHandler assertionResult,
        ExceptionCaughtEventHandler exceptionCaught,
        ErrorReporter errorReporter
    ):
        testStart_(testStart),
        testFinish_(testFinish),
        testAborted_(testAborted),
        testUnitStart_(testUnitStart),
        testUnitFinish_(testUnitFinish),
        testUnitSkipped_(testUnitSkipped),
        testUnitAborted_(testUnitAborted),
        assertionResult_(assertionResult),
        exceptionCaught_(exceptionCaught),
        errorReporter_(errorReporter)
    {
    }

    virtual void test_start(boost::unit_test::counter_t testCasesCount)
    {
        if (!testStart_(testCasesCount))
            throw execution_cancelled();
    }

    virtual void test_finish()
    {
        if (!testFinish_())
            throw execution_cancelled();
    }

    virtual void test_aborted()
    {
        if (!testAborted_())
            throw execution_cancelled();
    }

    virtual void test_unit_start(const boost::unit_test::test_unit &tu)
    {
        if (!testUnitStart_(ToWide(tu.p_name.get()).c_str(), tu.p_type.get() == boost::unit_test::tut_suite))
            throw execution_cancelled();
    }

    virtual void test_unit_finish(const boost::unit_test::test_unit &tu, unsigned long elapsed)
    {
        if (!testUnitFinish_(ToWide(tu.p_name.get()).c_str(), tu.p_type.get() == boost::unit_test::tut_suite, elapsed))
            throw execution_cancelled();
    }

    virtual void test_unit_skipped(const boost::unit_test::test_unit &tu)
    {
        if (!testUnitSkipped_(ToWide(tu.p_name.get()).c_str(), tu.p_type.get() == boost::unit_test::tut_suite))
            throw execution_cancelled();
    }

    virtual void test_unit_aborted(const boost::unit_test::test_unit &tu)
    {
        if (!testUnitAborted_(ToWide(tu.p_name.get()).c_str(), tu.p_type.get() == boost::unit_test::tut_suite))
            throw execution_cancelled();
    }

    virtual void assertion_result(bool passed)
    {
        if (!assertionResult_(passed))
            throw execution_cancelled();
    }

    virtual void exception_caught(const boost::execution_exception &ex)
    {
        std::string str(ex.what().begin(), ex.what().end());
        if (!exceptionCaught_(ToWide(str).c_str()))
            throw execution_cancelled();
    }

private:
    TestStartEventHandler testStart_;
    TestEventHandler testFinish_;
    TestEventHandler testAborted_;
    TestUnitEventHandler testUnitStart_;
    TestUnitFinishedEventHandler testUnitFinish_;
    TestUnitEventHandler testUnitSkipped_;
    TestUnitEventHandler testUnitAborted_;
    AssertionResultEventHandler assertionResult_;
    ExceptionCaughtEventHandler exceptionCaught_;
    ErrorReporter errorReporter_;
};
//------------------------------------------------------------------------------
extern "C" __declspec(dllexport) void BoostTestRun(
    const wchar_t* fileName,
    Visitor testCaseVisitor,
    Visitor beginTestSuiteVisitor,
    Visitor endTestSuiteVisitor,
    Visitor shouldSkipVisitor,
    TestStartEventHandler testStart,
    TestEventHandler testFinish,
    TestEventHandler testAborted,
    TestUnitEventHandler testUnitStart,
    TestUnitFinishedEventHandler testUnitFinish,
    TestUnitEventHandler testUnitSkipped,
    TestUnitEventHandler testUnitAborted,
    AssertionResultEventHandler assertionResult,
    ExceptionCaughtEventHandler exceptionCaught,
    ErrorReporter errorReporter
)
{
    HINSTANCE h = NULL;

    try
    {
        InitFunc initFunc = NULL;

        if (!(initFunc = FindInitFunc(h, fileName, errorReporter)))
            return;
       
        char *argv = "";
        boost::unit_test::framework::init(initFunc, 0, &argv);

        // Feed test tree to the caller.
        TestsVisitor visitor(testCaseVisitor, beginTestSuiteVisitor, endTestSuiteVisitor, shouldSkipVisitor);
        
        try
        {
            boost::unit_test::traverse_test_tree(
                boost::unit_test::framework::master_test_suite(),
                visitor
            );

            // Run tests.
            TestObserver observer(
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
            boost::unit_test::framework::register_observer(observer);
            boost::unit_test::framework::run();
        }
        catch (const execution_cancelled&)
        {
        }

        boost::unit_test::framework::clear();
        FreeLibrary(h);
    }
    catch (const std::exception &ex)
    {
        errorReporter(ToWide("Boost.Test error: " + std::string(ex.what())).c_str());
        CleanUp(h);
    }
    catch (const boost::execution_exception &ex)
    {
        errorReporter(ToWide("Boost.Test error: " + GetWhat(ex)).c_str());
        CleanUp(h);
    }
    catch (...)
    {
        errorReporter(L"Boost.Test framework internal error: unknown reason");
        CleanUp(h);
    }
}
//------------------------------------------------------------------------------
