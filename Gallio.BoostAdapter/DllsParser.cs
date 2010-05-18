using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Reflection;

namespace Gallio.BoostAdapter.Utils
{
    internal struct DllInfo
    {
        private ProcessorArchitecture architecture_;
        private IList<string> importedDlls_;

        public ProcessorArchitecture Architecture { get { return architecture_; } }

        public IList<string> ImportedDlls { get { return importedDlls_; } }

        public DllInfo(ProcessorArchitecture architecture, IList<string> importedDlls)
        {
            architecture_ = architecture;
            importedDlls_ = importedDlls;
        }
    }

    /// <summary>
    /// Utility functions for DLL files information retrieval without loading them.
    /// </summary>
    /// <note>
    /// It appears Gallio already has similar functionality in Gallio.Common.Reflection.AssemblyMetadata,
    /// should we just merge import table functionality there?
    /// </note>
    internal static class DllParser
    {
        /// <summary>
        /// Parses PE header and import table of given DLL.
        /// </summary>
        /// <returns>
        /// Dll information structure.
        /// </returns>
        public static DllInfo GetDllInfo(string dllPath)
        {
            try
            {
                using (FileStream fs = new FileStream(dllPath, FileMode.Open))
                {
                    return GetDllInfo(fs);
                }
            }
            catch (IOException ex)
            {
                throw new InvalidProgramException("Bad DLL image", ex);
            }
        }

        /// <summary>
        /// Parses PE header and import table of given DLL.
        /// </summary>
        /// <returns>
        /// Dll information structure.
        /// </returns>
        public static DllInfo GetDllInfo(Stream stream)
        {
            try
            {
                BinaryReader br = new BinaryReader(stream);

                stream.Seek(0x3C, SeekOrigin.Begin);
                UInt32 peOffset = br.ReadUInt32();

                stream.Seek(peOffset, SeekOrigin.Begin);
                UInt32 peHead = br.ReadUInt32();

                if (peHead != 0x00004550) // "PE\0\0", little-endian
                {
                    throw new InvalidProgramException("Bad DLL image: cannot find PE header!");
                }

                ProcessorArchitecture arch;
                UInt16 machineType = br.ReadUInt16();

                // We support very limited number of architectures - no need to define
                // full enum for our purposes.
                switch (machineType)
                {
                    case 0x8664: // IMAGE_FILE_MACHINE_AMD64
                        arch = ProcessorArchitecture.Amd64;
                        break;

                    case 0x200: // IMAGE_FILE_MACHINE_IA64
                        arch = ProcessorArchitecture.IA64;
                        break;

                    case 0x14c: // IMAGE_FILE_MACHINE_I386
                        arch = ProcessorArchitecture.X86;
                        break;

                    default:
                        arch = ProcessorArchitecture.None;
                        break;
                }

                stream.Seek(peOffset + 4 + 2, SeekOrigin.Begin);
                UInt16 sectionsCount = br.ReadUInt16();

                stream.Seek(peOffset + 4 + 16, SeekOrigin.Begin);
                UInt16 optHeaderSize = br.ReadUInt16();

                // No optional header - unable to read imports.
                if (optHeaderSize == 0)
                {
                    return new DllInfo(arch, null);
                }

                stream.Seek(peOffset + 4 + 20, SeekOrigin.Begin);
                UInt16 optMagic = br.ReadUInt16();
                UInt32 dirEntriesNumOffset, importTableOffset;

                switch (optMagic)
                {
                    case 0x10B: // PE32
                        dirEntriesNumOffset = 92;
                        importTableOffset = 104;
                        break;

                    case 0x20B: // PE32+
                        dirEntriesNumOffset = 108;
                        importTableOffset = 120;
                        break;

                    default:
                        // Unknown PE magic - cannot parse imports.
                        return new DllInfo(arch, null);
                }

                stream.Seek(peOffset + 4 + 20 + dirEntriesNumOffset, SeekOrigin.Begin);
                UInt32 dirEntriesNum = br.ReadUInt32();

                // Too small optional header size, import table is not present.
                if (dirEntriesNum < 2)
                {
                    return new DllInfo(arch, null);
                }

                stream.Seek(peOffset + 4 + 20 + importTableOffset, SeekOrigin.Begin);
                UInt32 importTableRVA = br.ReadUInt32();

                UInt32 sectionOffset = peOffset + 4 + 20 + optHeaderSize;
                Int32 importRawCorrection = 0, importRawOffset = 0;
                bool foundSection = false;

                // We need to perform translation from RVAs to raw file positions to successfully parse
                // import table entries - therefore loop through all sections and find the one that
                // matches importTableRVA - we will use it for addresses translation.
                for (UInt16 curSection = 0; curSection < sectionsCount; ++curSection, sectionOffset += 40)
                {
                    stream.Seek(sectionOffset + 8, SeekOrigin.Begin);
                    UInt32 virtualSize = br.ReadUInt32();

                    stream.Seek(sectionOffset + 12, SeekOrigin.Begin);
                    UInt32 virtualAddress = br.ReadUInt32();

                    stream.Seek(sectionOffset + 20, SeekOrigin.Begin);
                    UInt32 rawOffset = br.ReadUInt32();

                    if (virtualAddress <= importTableRVA && importTableRVA < virtualAddress + virtualSize)
                    {
                        importRawCorrection = (Int32)(virtualAddress - rawOffset);
                        importRawOffset = (Int32)(importTableRVA - importRawCorrection);
                        foundSection = true;
                        break;
                    }
                }

                // No section matching import table RVA found
                if (!foundSection)
                {
                    return new DllInfo(arch, null);
                }

                List<string> imports = new List<string>();

                // Loop through import table and accumulate or DLLs names mentioned there.
                for (UInt32 entryOffset = (UInt32)importRawOffset; true; entryOffset += 20)
                {
                    stream.Seek(entryOffset + 12, SeekOrigin.Begin);
                    UInt32 importNameOffset = br.ReadUInt32();

                    // End of import table - stop processing.
                    if (importNameOffset == 0)
                    {
                        break;
                    }

                    importNameOffset = (UInt32)(importNameOffset - importRawCorrection);

                    stream.Seek(importNameOffset, SeekOrigin.Begin);
                    List<byte> bytes = new List<byte>();
                    byte b;
                    while ((b = br.ReadByte()) != 0)
                    {
                        bytes.Add(b);
                    }

                    imports.Add(Encoding.ASCII.GetString(bytes.ToArray()));
                }

                return new DllInfo(arch, imports);
            }
            catch (IOException ex)
            {
                throw new InvalidProgramException("Bad DLL image", ex);
            }
        }
    }
}
