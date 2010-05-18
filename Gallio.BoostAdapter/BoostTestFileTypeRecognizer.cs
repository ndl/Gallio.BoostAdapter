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

using Gallio.Runtime.FileTypes;
using System;
using System.IO;

namespace Gallio.BoostAdapter.Utils
{
    class BoostTestFileTypeRecognizer: IFileTypeRecognizer
    {
        public bool IsRecognizedFile(IFileInspector fileInspector)
        {
            Stream stream;

            if (fileInspector.TryGetStream(out stream))
            {
                // Parse PE header/load import table.
                DllInfo info = Gallio.BoostAdapter.Utils.DllParser.GetDllInfo(stream);

                // No import table? Certainly not Boost.Test testable DLL then.
                if (info.ImportedDlls == null)
                {
                    return false;
                }

                // If there's reference to Boost.Test DLL - assume the DLL is testable.
                // This is not 100% correct - presence of test initialization function
                // should also be checked to be completely sure it can be tested, but
                // that would complicate things too much while providing little extra
                // benefits.
                foreach (string DllName in info.ImportedDlls)
                {
                    if (DllName.StartsWith(
                            "boost_unit_test_framework",
                            StringComparison.InvariantCultureIgnoreCase))
                    {
                        return true;
                    }
                }
            }

            return false;
        }
    }
}
