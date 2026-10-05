# Nodalis development rules

These rules apply to every C# change in this repository.

## C# typing

- Do not use `var`.
- Always write the explicit static type, including when the type is obvious from the right-hand side.
- Do not introduce anonymous types that would force the use of `var`; use a named record/class/struct instead.
- The only acceptable exception is a language construct for which C# provides no nameable explicit type. Such an exception must be documented inline and should be refactored away whenever practical.

## XML documentation

- Every C# method, constructor, destructor, operator, and conversion operator under `src/` must have XML documentation.
- Use `/// <summary>` to state what the member does.
- Document every generic type parameter with `<typeparam>`.
- Document every parameter with `<param>`.
- Document non-`void` return values with `<returns>`.
- Keep documentation behavioral and useful; do not merely repeat the signature when a clearer explanation is available.
- Local functions cannot reliably participate in generated XML documentation; use a normal explanatory comment when a local function is non-obvious.

## Verification

Before considering a C# change complete, run:

```powershell
dotnet run --project tools/Nodalis.CodeStyle/Nodalis.CodeStyle.csproj --configuration Release -- --check
dotnet build Nodalis.sln --configuration Release
dotnet run --project tests/Nodalis.SmokeTests/Nodalis.SmokeTests.csproj --configuration Release
```

The CI enforces the explicit-type rule and verifies XML documentation coverage for C# members.
