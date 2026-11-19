# Third-party notices

AMZ CLI is licensed under the Apache License, Version 2.0. See `LICENSE`.

The distributed binaries include or depend on the following components.

## .NET runtime

The self-contained Linux and Windows builds include the .NET 10 runtime.

- License: MIT
- Project: <https://github.com/dotnet/runtime>
- License text: <https://github.com/dotnet/runtime/blob/main/LICENSE.TXT>

## NuGet packages

| Component | Version | License | Project |
|---|---|---|---|
| Spectre.Console | 0.57.2 | MIT | <https://github.com/spectreconsole/spectre.console> |
| Spectre.Console.Cli | 0.57.2 | MIT | <https://github.com/spectreconsole/spectre.console> |
| CurlImpersonate.Http | 1.0.0 | MIT | <https://github.com/tcortega/CurlImpersonate> |
| CommunityToolkit.WinUI.Notifications | 7.1.2 | MIT | <https://github.com/CommunityToolkit/WindowsCommunityToolkit> |
| System.Drawing.Common | 10.0.15 | MIT | <https://github.com/dotnet/winforms> |

## Native libraries in the Windows package

The Windows package includes `libcurl-impersonate` and its native
dependencies. The corresponding license texts are shipped in the
`libcurl-impersonate-licenses/` directory:

- curl
- BoringSSL
- brotli
- c-ares
- nghttp2
- nghttp3
- ngtcp2
- zlib
- zstd

The Linux build uses the system `curl-impersonate` command; it is not bundled.

## Development and test dependencies

xUnit, coverlet, Microsoft.NET.Test.Sdk, and their transitive dependencies are
used only for development and testing. They are not part of the released CLI
packages.

The MIT license text is available at
<https://opensource.org/license/mit>.
