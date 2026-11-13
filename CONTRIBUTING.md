# Contributing

## Game dependencies

Install .NET 10 and Git with LFS. Fetch expects a bare repository at
`../probably-stolen-assemblies` and creates a detached worktree at
`./probably-stolen-assemblies`. The worktree is ignored by the mod repository.
It contains `lib/`, `src/`, and `snapshot.json` for the selected game snapshot.

With access to a private snapshot repository, clone it once:

```powershell
git clone --bare --filter=blob:none YOUR-PRIVATE-REPOSITORY-URL ../probably-stolen-assemblies
.\fetch.ps1 -Revision 100-Rev14
```

Without access to an existing repository, initialize your own and import your
installed game. A remote is optional:

```powershell
git init --bare ../probably-stolen-assemblies
.\fetch.ps1 -Installed
```

Save your usual arguments in ignored `game-cache.local.json`:

```json
{
  "repository": "../probably-stolen-assemblies",
  "worktreePath": "./probably-stolen-assemblies",
  "revision": "100-Rev14"
}
```

All three settings are optional. The two paths above are the defaults; omitting
`revision` selects the installed game. Optional `gameDir` overrides Steam discovery
and `propsPath` overrides the generated `GameReferences.local.props` location.
Paths are relative to the directory containing `fetch.ps1`, even when called from
elsewhere. Command-line arguments override the config file. Import/push/signing
switches remain explicit command-line choices.

```powershell
# Use the saved configuration.
.\fetch.ps1
# Override the selected display-version, unity-version, or full commit ID.
.\fetch.ps1 -Revision 100-Rev13
.\fetch.ps1 -Revision 0.46D
# Explicitly refresh existing tags from origin.
.\fetch.ps1 -Refresh

# Compare the installed game's actual DLLs, even when its labels already exist.
.\fetch.ps1 -Installed
# Import and publish the branch and both tags atomically.
.\fetch.ps1 -Installed -Push

dotnet build Tuck.slnx
dotnet test Tuck.slnx
.\tools\GameSnapshots.Tests.ps1
.\package.ps1 -c Release
.\deploy.ps1 -GameDir 'C:\path\to\Probably Stolen'
```

`-Repository` takes an existing local bare repository path. A missing tag can be
imported from the installation only when it matches one of that game's labels;
a missing commit ID or mismatched tag is an error. Import respects Git's signing
configuration; `-Unsigned` explicitly disables signing for that import. Imports
are committed directly to the bare repository. `-Installed -Push` also publishes
the branch and both tags to its `origin`, if configured.

All mods using the same bare repository share its Git objects and committed
decompiled sources. Each mod has its own worktree, so they can select different
versions. Fetch updates that worktree in place when switching versions; finish
active builds first. Fetch discards local edits and generated files in its worktree,
restoring the selected snapshot. It refuses to overwrite unrelated directories.
Available local snapshots, including tags, are selected without a remote refresh
or starting .NET. Fetch contacts `origin` when the requested revision is missing,
when importing an installation, or with `-Refresh`. To pick up a moved remote tag,
use `-Refresh`. A partial clone may download missing files on the first checkout
of a snapshot, without checking out every version. Unchanged build properties
are left untouched. The .NET version reader and decompiler are used only during
imports; committed sources are reused whenever the DLL contents are already known.

Every unity-version has a branch. Both version labels are tags pointing to the
latest imported snapshot with those labels. Identical labels with changed DLLs
append a commit; identical content at the branch tip does not. Historical commits
remain reachable through branches. When DLL contents recur, import reuses the
committed decompiled sources, including their recorded decompiler version.
Pin full commit IDs for reproducible builds. Ordinary Git stores both DLLs
and sources; this repository does not use LFS.

Fetch writes ignored `GameReferences.local.props` for all game-dependent projects.
Builds never fetch dependencies or search for a game installation. Their output
and intermediate directories include the snapshot commit. For concurrent builds
in one mod checkout, give each fetch distinct `-WorktreePath` and `-PropsPath`
locations under `artifacts/`, then pass the props file's absolute path as
`-p:GameReferenceProps=...` to each build/test. Mods
can also consume the selected `lib/` directly; Tuck accepts explicit
`-p:GameAssembliesDir=... -p:GameUnityVersion=...` properties.

## Projects

The solution contains seven projects:

| Project | Responsibility |
| --- | --- |
| `Tuck.Mod` | Game capture, solver adapter, plan execution, controls and Debug recording. |
| `Tuck.Mod.Tests` | Game-boundary and execution tests using explicit plans, plus Debug recording tests. No solver discovery or optimization assertions. |
| `Tuck.PackSolver` | Detached packing contracts, derivation, replay and solver implementations. |
| `Tuck.PackSolver.Tests` | All solver correctness, search, policy and replay tests, including scenarios imported from recordings. |
| `Tuck.PackSolver.Benchmarks` | Solver performance and differential workloads. |
| `Tuck.ProbablyStolenTestModel` | Independent, headless game inventory model for test fixtures. No solver or mod dependency. |
| `Tuck.ProbablyStolenTestModel.Tests` | Model behavior and optional parity checks against fetched game assemblies. |
