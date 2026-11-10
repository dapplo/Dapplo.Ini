# Dapplo.Ini.WindowsPolicies.Tool

`dapplo-ini-admx` generates an ADMX/ADML administrative template (and optionally a `.reg` file) for the
[Dapplo.Ini](https://www.nuget.org/packages/Dapplo.Ini) sections of an application assembly, ready for the Group Policy
central store, gpedit, GPMC and Intune ("Import ADMX").

```shell
dotnet tool install -g Dapplo.Ini.WindowsPolicies.Tool
dapplo-ini-admx --assembly bin/Release/net10.0-windows/MyApp.dll --vendor Dapplo --product MyApp \
                --output policies --supported-on "MyApp 2.0 or later" --language en-US --language de-DE
```

Writes `policies/<prefix>.admx` and `policies/<lang>/<prefix>.adml`. Options: `--prefix`, `--namespace`,
`--revision`, `--root-path`, `--section`, `--no-recommended`, `--reg <file>`; `--help` lists them.
Point it to a .NET (not .NET Framework) build of the application.

See the wiki page [Windows Policies](https://github.com/dapplo/Dapplo.Ini/wiki/Windows-Policies).
