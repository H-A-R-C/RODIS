# Third-party software notices

RODIS includes or depends on third-party software. RODIS itself is licensed under the GNU General Public License version 3, but the components listed below remain subject to their own copyright notices and licence terms.

This file describes the direct dependencies used by RODIS.Core 2.2. The authoritative licence files supplied with each NuGet package and with the GDAL native distribution should also be retained in binary release archives.

## Runtime dependencies

### CsvHelper 33.1.0

Project: [CsvHelper](https://joshclose.github.io/CsvHelper/)

Licence: Apache License 2.0, selected from the package's dual Apache-2.0 / Microsoft Public License offering.

Copyright belongs to the CsvHelper authors and contributors.

A copy of the Apache License 2.0 must accompany distributions containing CsvHelper. The licence is available from [Apache License 2.0](https://www.apache.org/licenses/LICENSE-2.0).

### GDAL 3.11.3 and GDAL.Native 3.11.3

Project: [Geospatial Data Abstraction Library](https://gdal.org/)

Licence: MIT-style licence for GDAL/OGR. Some files and native dependencies distributed with GDAL use other permissive or weak-copyleft licences.

Copyright belongs to the Open Source Geospatial Foundation and other GDAL contributors.

The complete licence information supplied with the GDAL binary distribution must be retained. In the current Windows build this includes, at minimum:

- `gdal/GDALLicense.rtf`
- `gdal/GISInternalsLicense.rtf`
- `gdal/license.txt`
- `gdal/data/LICENSE.TXT`

The contents of a GDAL binary distribution depend on how GDAL was built. Before publishing a RODIS binary release, the final clean publish directory must be checked for optional or proprietary drivers and SDKs. In particular, RODIS releases must not include ECW, MrSID or FileGDB plugin binaries unless their separate redistribution terms have been reviewed and satisfied.

### NCalc 1.3.8

Project: [NCalc](https://github.com/ncalc/ncalc)

Licence: MIT License.

Copyright (c) 2011 Sebastien Ros

Permission is hereby granted, free of charge, to any person obtaining a copy of
this software and associated documentation files (the "Software"), to deal in
the Software without restriction, including without limitation the rights to
use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies
of the Software, and to permit persons to whom the Software is furnished to do
so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.

NCalc 1.3.8 is a deprecated legacy package and is no longer maintained. It is
retained by RODIS for compatibility with the existing expression evaluation
implementation.

### Newtonsoft.Json 13.0.4

Project: [Json.NET](https://www.newtonsoft.com/json)

Licence: MIT License.

Copyright (c) 2007 James Newton-King.

Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files (the "Software"), to deal in the Software without restriction, including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.

### UnitsNet 5.75.0

Project: [UnitsNet](https://github.com/angularsen/UnitsNet)

Licence: MIT No Attribution (MIT-0).

Copyright 2013 Andreas Gullberg Larsen.

Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files (the "Software"), to deal in the Software without restriction, including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.

## Build and test dependencies

The following packages are used to build, analyse or test RODIS and are not intended to form part of the RODIS.Core runtime distribution:

- StyleCop.Analyzers 1.1.118, MIT License
- Microsoft.NET.Test.Sdk and associated Microsoft Testing Platform components, MIT License
- MSTest.TestAdapter and MSTest.TestFramework, MIT License

`StyleCop.Analyzers` should be referenced with `PrivateAssets="all"` so it is not exposed as a transitive dependency of RODIS.

## .NET runtime

Framework-dependent releases require users to install a compatible .NET runtime separately. Self-contained releases include Microsoft .NET runtime components and must retain the licence and third-party notice files generated by `dotnet publish` for those components.

## No relicensing of third-party components

The inclusion of a third-party component in a GPL-3.0-licensed RODIS distribution does not change the component's own licence. Third-party names and trademarks remain the property of their respective owners.
