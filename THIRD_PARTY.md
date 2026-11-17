# Third-party components

## S7CommPlusDriver
- Path: `third_party/S7CommPlusDriver` (sources copied, built by `src/S7CommPlusDriver/S7CommPlusDriver.csproj`)
- Upstream: https://github.com/thomas-v2/S7CommPlusDriver (base commit `dbd61e4`)
- Copyright: Thomas Wiens
- License: GNU Lesser General Public License v3.0, see `third_party/S7CommPlusDriver/LICENSE`
- Local changes on top of upstream (in `src/S7CommPlusDriver/Net/S7Client.cs`):
  1. `RunThread()` catches exceptions so a socket error cannot terminate the host process.
  2. The TLS key-log callback is not registered by default and never throws across the native boundary
     (upstream wrote TLS session keys to `key_*.log` in the working directory).
  3. `ClientApi/Browser.cs`: arrays of FB instances (`POffsetInfoType_FbArray`, a TODO upstream) are expanded
     per element instead of being treated as one instance. The access sequence carries a placeholder after
     the array index, which `PlcReader` resolves by probing the PLC.
- The driver is a community, reverse-engineered implementation of the S7CommPlus protocol. It is not
  provided or supported by Siemens.

## Zlib.net
- Path: `third_party/S7CommPlusDriver/src/Zlib.net` (shipped with the driver), compiled into `S7CommPlusDriver.dll`
- License: see `third_party/S7CommPlusDriver/src/Zlib.net/LICENSE`

## OpenSSL 3 (binaries)
- `libssl-3-x64.dll`, `libcrypto-3-x64.dll`, shipped with the driver and copied next to the exe
- License: Apache License 2.0, https://www.openssl.org/source/license.html

This program itself is licensed under LGPL-3.0 as well (see `LICENSE`).
