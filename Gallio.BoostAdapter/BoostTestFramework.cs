using Gallio.Model;
using Gallio.Runtime.Extensibility;
using Gallio.Runtime.Logging;

using System.Collections.Generic;

namespace Gallio.BoostAdapter.Model
{
    class BoostTestFramework: BaseTestFramework
    {
        public override TestDriverFactory GetTestDriverFactory()
        {
            return CreateTestDriver;
        }

        private ITestDriver CreateTestDriver(
            IList<ComponentHandle<ITestFramework, TestFrameworkTraits>> testFrameworkHandles,
            TestFrameworkOptions testFrameworkOptions,
            ILogger logger
        )
        {
            return new BoostTestDriver(logger);
        }
    }
}
