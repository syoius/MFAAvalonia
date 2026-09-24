# AGENTS.md

## Project and working mode

This is the desktop integration of the syoius/MFAAvalonia fork with upstream MFAAvalonia. Default to communicating with the user in Chinese. These instructions are written for Astra and other capable coding agents: use judgment, pursue the requested outcome, and keep process proportional to the work. They do not select a model or reasoning setting.

The current user request determines whether you are implementing, reviewing, or planning. In implementation mode, finish the authorized implementation, necessary verification, and requested handoff; a plan alone is not completion. In review or analysis mode, do not change production behavior. Separate a discovered defect from its proposed fix.

Treat decisions already settled in the conversation and accepted project records as settled. Resolve routine implementation choices yourself. Ask only for missing information that materially changes behavior, authority, or an irreversible action; continue independent work while waiting. Existing user authorization takes precedence over this file.

## Repository and plan

- Start by identifying the actual worktree, branch, changes, and relevant local instructions. Preserve other people's changes and untracked documents. Do not reset, clean, stash, or change branches just to make the workspace look clean.
- The established desktop integration worktree is `C:\MFAAvalonia\.worktrees\desktop-p0-p3`; the original worktree is `C:\MFAAvalonia`. Use the worktree named by the latest task. The directory name does not mean all future work must remain in P0–P3.
- Production changes and their delivery documents belong in the authorized integration worktree. Preserve original-worktree review documents. Changes to this guidance or original-worktree reports are allowed when the user requests them.
- Do not operate on the real installation `C:\MaaY` or migrate real user data for a test. Use isolated copies and test output directories. Read-only sample inspection requires the task to call for it.
- The implementation plan is `docs/zh/桌面端上游同步实施方案-2026-09-19.md`; the background comparison is `docs/zh/分支差异与上游同步评估-2026-09-19.md`. Read the relevant phase, current progress, and newest applicable review/handoff, not every historical report on every turn.
- `docs/zh/桌面端同步进度-2026-09-19.md`, `桌面端同步验收-2026-09-19.md`, and phase reports hold status and evidence. Do not hardcode a changing HEAD, test count, or phase verdict into this file.
- The plan pins upstream `a3c24837f4fe2733ad4c7855e29f03b24b86d407` and the old fork behavior baseline `366205f68e0389288eaafad9807b1d607b46862e`. Upstream's default branch is master. Do not refresh these baselines unless the task asks.
- Work only on the assigned phase and the shared code necessary for it. Stop at the requested phase review. A later passing review supersedes an earlier repair hold; preserve the historical reports without redoing accepted work.
- Local commits are appropriate when requested by the implementation task. Do not infer authorization to push, merge, tag, publish, upload, or modify the downstream MaaYuan repository.

## Product contracts

- Release targets are Windows x64 (`win-x64`) and Apple Silicon macOS (`osx-arm64`). Do not migrate, customize, or improve the upstream Android app. Preserve desktop ADB support. Shared files may contain Android branches; file location alone does not justify deleting them or expanding Android work.
- Users are mostly nontechnical. Normal upgrades must preserve data automatically; do not require routine JSON editing, manual export/import, or rebuilding timers.
- Cross-configuration scheduling is a used feature. Preserve the rule's instance, source-profile activation, target profile, force policy, schedule, and enabled state. Switching snapshots in the same instance must retain edits.
- Copilot state and execution snapshots are independent of home tasks. Same names, page changes, and shared resource definitions do not authorize sharing mutable instance state.
- Startup acceptance, actual task completion, UI state, and native cleanup are different events. All supported launch paths must respect the same instance/resource ownership. Stop callbacks and retries must remain usable under that contract.
- Shared resource aliases and overlapping directories occur in supported desktop use. Coordinate real resource paths and actual starting/running/stopping/recording occupancy; preserve user files and restore failed replacements.
- P6 global automatic exit/shutdown must wait for all instances and recording occupancy. Manual cancellation creates no new automatic request. Do not count a future phase as implemented because its interface exists.
- GUI artifacts use `syoius/MFAAvalonia` and MirrorChyan RID `YuanMFA`. End-user updates use the MaaYuan final resource package from `syoius/MaaYuan`, RID `MaaYuan`, and compare the resource version. Do not restore a separate GUI updater.
- The first upgrade runs the old updater before the new GUI migration. New startup migration cannot protect the earlier file-replacement stage; final-package upgrade acceptance must cover both.

## Scope and engineering judgment

These limits bound what you PROPOSE, never what you look for. Report anything actually wrong in supported use, including an uncommon input or sequence the project really produces.

1. Assume a cooperating operator on their own machine unless a real adversary or security boundary is stated. Do not turn a desktop feature task into general host/account hardening.
2. Do not add hashes, checksums, or fingerprints unless they replace a materially more expensive operation AND the result changes the next action. Do not generate unused checksum files or hash ordinary comparisons. Existing, explicitly requested migration/recovery or evidence requirements still apply; do not start a separate rewrite to remove accepted protections.
3. Prefer direct changes in existing architecture. Do not add flags, wrappers, generic migration frameworks, compatibility layers, or abstraction stacks for hypothetical cases. This project does require specific old-data compatibility, startup recovery, and execution ownership; implement that work without generalizing it beyond the actual contracts.
4. Reachability comes from supported inputs, published interfaces, documented examples, real data, or normal application paths. Reachable is enough; a reproduction is useful but not a prerequisite. A scenario constructible only by arbitrary internal-state corruption is not enough. Do not pursue exotic encodings, RTL issues, symlink races, or millisecond races without that connection. Ordinary multi-instance startup/stop and shared resource interactions are actual project behavior, not automatically exotic.
5. Make a judgment when judgment is needed. Do not substitute a scoring system, ritual checklist, excessive task decomposition, or repeated re-verification of settled facts. Do not force step-by-step reasoning dumps; state the decision and the evidence the user needs.
6. Explicit user requests and applicable higher-priority rules about migration, security, verification, or review remain the work. These limits do not excuse omitting them. Equally, do not expand the request just because a defensive measure can be imagined.

Calibration examples: hashing every row when comparing values suffices; hardening accounts for an app with no such deployment; spending the session re-auditing your patch while leaving the feature unwritten; requiring failure on every review; adding a guard justified only by the previous guard. These are patterns to avoid, not reasons to dismiss a real defect that happens to look similar. A digest that avoids repeatedly reading a large file, or an unusual input produced by a documented example, can be justified.

## Code Review Rules

- Review the changed behavior and its real callers, not only the new helper's happy path. Preserve supported home/Copilot/timer/CLI/global launch, saved settings, and failure semantics.
- For a finding, give the trigger, supported path, incorrect result, code location, and practical impact. Distinguish a confirmed defect, an unresolved uncertainty, and an optional improvement. Use focused reproductions when they resolve a material uncertainty.
- Keep fixes within the actual problem. A defect in stop-to-restart timing does not authorize rewriting the task engine; a missing data field does not automatically require a new schema framework.
- Accepted earlier work is a baseline, not an instruction to ignore regressions. Reopen it only when a new change, failure, or concrete reachability evidence warrants doing so.
- Say plainly when something is correct. Do not manufacture findings, require an arbitrary number of findings, or turn preferences into blockers.
- An external acceptance gap is not a newly discovered implementation defect. It may still block release or a specific dependent phase; explain that dependency. Native devices, Apple Silicon, real historical packages, and power-loss experiments do not need to be available to finish unrelated local development.
- Give an explicit phase verdict: pass, needs bounded repair, or awaiting specific evidence. Do not claim release acceptance from local builds or leave a perpetual review loop without a concrete unresolved reason.

## Verification and evidence

Before each check, answer internally: **What specific failure would this detect, and what would I do differently if it occurred?** If there is no answer, do not run it. This is a decision rule, not a request to print a checklist before each command.

- Run checks appropriate to the change and any explicitly required phase checks. Use a focused reproduction for a specific question and the existing suite for shared migration/runtime regressions. Documentation-only edits need reading and consistency checks, not application builds.
- Prefer assertions of behavior and effective saved values through production paths. Replace device/network side effects when appropriate; do not substitute an independent copy of the implementation and call it production verification.
- Once relevant checks pass, stop repeating them unless code changed, a failure appeared, or a material concern remains. Do not rerun unchanged builds merely to obtain another log or rehash the same evidence.
- Build/test output is evidence, not the objective. Counts do not replace behavior coverage. Never weaken a regression assertion merely to make a run green.
- Use concise reproducible records: commit/worktree, command, result, significant limitations, and relevant log location. Keep existing evidence; do not add hash inventories, duplicate reports, or new evidence infrastructure by default. Preserve or supply them when specifically requested.
- Separate static analysis, isolated/headless checks, native resource parsing, real device runs, clean-runner builds, and final-package upgrades. An osx-arm64 build on Windows is a cross-build, not an Apple Silicon execution test.
- Do not treat an incremental build's zero warnings or an offline restore as proof that historical dependency warnings disappeared.
- Keep external acceptance items visible in the phase record. Complete local work that does not depend on them; do not mark unperformed acceptance as passed.

## Practical commands

Use `rg` for searches. Batch independent reads; keep edits and dependent operations sequential. On this Windows workspace, use literal paths for filesystem operations and inspect targets before recursive moves/deletes. Prefer per-command `git -c safe.directory=<actual-worktree>` when ownership requires it; do not change global Git settings just for this task.

Desktop verification from the integration worktree:

```powershell
./scripts/test-desktop-compatibility.ps1 -NoRestore
dotnet build MFAAvalonia.Desktop/MFAAvalonia.Desktop.csproj -c Release -r win-x64 -p:MSBuildEnableWorkloadResolver=false -p:UsedAvaloniaProducts=
dotnet build MFAAvalonia.Desktop/MFAAvalonia.Desktop.csproj -c Release -r osx-arm64 -p:MSBuildEnableWorkloadResolver=false -p:UsedAvaloniaProducts=
```

Use `-NoRestore` only when the relevant assets exist. The two RIDs share intermediate assets; restore/build them sequentially. If the work requires both and ends on Windows, restore win-x64 assets for the next local check. Use installed fixed dependencies/offline cache as documented when needed; do not upgrade packages just to sidestep a restore problem. Do not require Android workloads for desktop verification.

## Handoff

Report the result first: changed behavior or review verdict, relevant validation, and remaining limitations. Include actionable file links and the branch/commit when useful. Update the appropriate phase record; avoid replacing progress with a large new process document. Preserve real unresolved issues, state when a phase can proceed, and stop at the user's requested boundary.

