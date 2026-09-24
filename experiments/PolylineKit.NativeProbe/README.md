# Optional C++ microkernel comparison

This Windows x64/AVX2 experiment compares the same compensated edge sum and
sorted AABB-pair loop in safe C# arrays, C# pointers and C++ through P/Invoke.
It is not a native geometry backend and is not part of the solution, library or
runtime dependencies. Inputs, operation order and result checks are in the source.

From a Visual Studio x64 Native Tools command prompt in this directory:

```text
build-native.cmd
dotnet build -c Release
```

Then, in PowerShell, using an otherwise idle machine:

```powershell
$env:DOTNET_TieredCompilation = '0'
dotnet bin/Release/net10.0/PolylineKit.NativeProbe.dll
1..3 | ForEach-Object {
    dotnet bin/Release/net10.0/PolylineKit.NativeProbe.dll measure > "run-$_.jsonl"
}
```

The initial invocation checks identical edge sums and candidate counts. The
measurement rotates variant order across nine samples per workload. Native
transitions are included; buffers are pinned once outside measurements, and no
input copy is included. A public list-based native adapter may cost more.

Do not use `/fp:fast`: reassociation can invalidate TwoDiff tails. The measured
compiler was MSVC 14.51, with `/O2 /fp:precise /arch:AVX2`, without `/fp:contract`.
The whole winding engine has not been ported or measured in C++.

See [performance assessment](../../docs/winding-performance.md) and
[recorded microkernel results](../../results/winding/performance/native/results.md).
