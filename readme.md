                      Gallio Boost.Test Adapter
                      =========================
              Copyright (C) 2010 Alexander Tsvyashchenko,
                        http://www.ndl.kiev.ua
              Licensed under the Apache License, Version 2.0

Rationale
---------

Boost.Test framework is natural solution for native C++ code unit testing
that already uses boost libraries, and possibly even for the code that doesn't -
though then there are a lot of other alternatives worth considering.

However, as it supports native code only, when using mixed-mode projects
consisting of C++ and code in some of the .NET languages, there's the need to
use another testing framework for managed code - and, consequently, run
those tests.

I wasn't able to find any existing solution which would allow uniformly
running both Boost.Test tests and any .NET testing framework tests, where by
"uniformity" I mean using single interface/configuration to specify what tests
to run, using single runner to actually run them and getting single report for
both native/managed tests.

Gallio is good solution for running multiple tests in all major .NET testing
frameworks so I decided the easiest way to achieve what I want is to add
Boost.Test support to Gallio.

Due to Gallio being designed/used exclusively for .NET testing frameworks it
appeared adding native testing framework poses some special challenges, but so
far it looks like I was able to overcome most of them - and even without
changing code of Gallio itself, which seems to indicate their extension
mechanism is quite extendable indeed ;-)

Installation and usage
----------------------

1. Either fetch [Gallio.BoostAdapter sources](http://github.com/ndl/Gallio.BoostAdapter) from github and build them (the preferred way) or download [compiled Gallio.BoostAdapter binaries](http://www.ndl.kiev.ua/downloads/Gallio.BoostAdapter-0.0.1-bin.zip) (compiled against boost 1.43.0 using VS 2010, see below for pitfalls!)
2. Put binaries to the Gallio directory (the best is to put it into sub-directory to avoid cluttering the main Gallio directory),
on the next run Gallio will detect Gallio.BoostAdapter and start to use it for
DLLs in testing solution that are testable using Boost.Test.

Tested compilers are VS 2008/2010, tested boost versions are 1.42.0 and 1.43.0

Boost.Test framework DLL dependency
-----------------------------------

Gallio.BoostAdapter.Native.dll requires Boost.Test framework DLL to operate.

Provided binaries contain Boost.Test framework version compiled with
SECURE_SCL defined to 0 and require testable DLL to use the same
Boost.Test framework version and the same SECURE_SCL define value.

If testable DLL uses different Boost.Test framework version and/or SECURE_SCL
define value - you should replace provided DLLs with your ones and recompile
Gallio.BoostAdapter.Native.dll

Known bugs/limitations
----------------------

1. Due to incompatibilities between different VS runtime versions it's necessary
that Boost.Test library, tested DLL and Gallio.BoostAdapter itself be compiled
using the same compiler - I was unable to reuse
VS 2008 compiled Boost.Test library and Gallio.BoostAdapter
with VS 2010 compiled tested DLL.

2. The information provided by Boost.Test is limited compared to what Gallio could
use - for example, there seems to be no way to get source files locations
of Boost.Test asserts to show them in Gallio without modifying Boost.Test
framework code.

3. So far I was unable to force VS2008/VS2010 plus Gallio to recognize
Boost.Test projects in solution as testable, so it's not possible to run
Boost.Test tests using VS test runner - not sure whose "fault" it is,
but it may well appear that it's VS limitation and then we're out
of options, I'm afraid.

4. Due to a bunch of reasons, such as: extra efforts required to interact
between native/managed code; Boost.Test architecture not mapping exactly
to Gallio one; lack of support of native code testing in Gallio - things were
done in many places differently to what would be "natural" in Gallio framework,
in some places sacrifising beauty/uniformity/performance.

For example, instead of using "common" Gallio approach of Gallio being the
controller of tests to run (and reusing existing support classes for that)
the information about tests to run is fed to Boost.Test and then Boost.Test
performs all selected tests execution and Gallio just receives back status
information for each executed test. This distinction might seem minor, but it
translates to completely different implementation compared to other test
frameworks extensions in Gallio. The same counts for tests exploration and
representation: as native tests are not discoverable by reflection and not
representable as Gallio ICodeElementInfo objects, I wasn't able to reuse
Gallio support classes for that task either.

Architecture
------------

Gallio.BoostAdapter consists of two components:

 * Gallio.BoostAdapter.dll - managed assembly that implements interfaces
   required by Gallio and loads/communicates with Gallio.BoostAdapter.Native.dll
 * Gallio.BoostAdapter.Native.dll - unmanaged DLL that bridges
   Gallio.BoostAdapter with Boost.Test and testable DLL.

The solution with single mixed mode DLL written in C++/CLI was considered but
determined to be inappropriate in this situation:

 1. It has problems with isolation, most likely related to native C
    runtime going crazy when performing the same DLL loading in isolated context.
 2. On the other hand, not using isolation is not only less safe (as single
    failed test would break all tests in the project) but also seems to be not
    possible at all with current version of Boost.Test as it has problems
    performing multiple init/clear calls that are required to test multiple
    testable DLLs in single process.
 3. It would be harder to implement multiple architectures/configurations
    support as we would have to switch somehow from one
    architecture/configuration DLL to another one each time isolation starts.

Different processor architectures/configurations support
--------------------------------------------------------

Gallio.BoostAdapter.Native.dll needs to match the architecture/configuration
of the testable DLL so that it can successfully load/test it. This means
Gallio.BoostAdapter.dll has to determine architecture/configuration of testable
DLL prior to loading it - and then calling the appropriate version of
Gallio.BoostAdapter.Native.dll

The required check is implemented by parsing PE header to determine architecture
and by parsing import table looking for Boost.Test dll name and inferring
configuration from its name.
