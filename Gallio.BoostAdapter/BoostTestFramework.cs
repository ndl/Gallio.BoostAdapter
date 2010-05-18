﻿// Copyright 2010 Alexander Tsvyashchenko - http://www.ndl.kiev.ua
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
