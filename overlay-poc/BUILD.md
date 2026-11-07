# BUILD.md — Building OverlayPoc via GitHub Actions

This is the playbook for building this project and producing a downloadable
installer artifact **by pushing to GitHub**. Do **not** try to build locally
with `dotnet build` unless a .NET 8 SDK is actually installed — the build
machine is GitHub's hosted runner, not this workstation.

## Facts about this repo

| Item          | Value                                                        |
| ------------- | ------------------------------------------------------------ |
| Owner/repo    | `nathanieluriri/opencluely-overlay-test`                     |
| Branch        | `master`                                                     |
| Workflow      | `Build OverlayPoc` (already configured on GitHub)            |
| Triggers on   | every push to `master`                                       |
| Artifact name | `OverlayPoc`                                                 |
| Typical time  | ~40–60 seconds                                               |
| Output        | published WinForms build (unzip and run `OverlayPoc.exe`)    |

## The 3-step build process

### 1. Commit and push

Run from the repo root (`git rev-parse --show-toplevel` should resolve to the
`overlay-poc` directory).

```powershell
git add -A
git commit -m "fix: describe the change here"
git push
```

- `git add -A` stages everything changed. If you only touched one file, that
  is still fine to use — the rest of the tree is already committed.
- `LF will be replaced by CRLF` warnings are harmless. Ignore them.
- Never put secrets in the commit message or in tracked files.

### 2. Wait for the workflow run

The push itself kicks off the build. Poll until its status is `completed`:

```powershell
Start-Sleep -Seconds 35
gh run list --repo nathanieluriri/opencluely-overlay-test --limit 1 --branch master
```

Expected output looks like:

```
completed  success  fix: describe the change here  Build OverlayPoc  master  push  <RUN_ID>  47s  <timestamp>
```

If the status is still `in_progress`/`queued`, wait and run the `gh run list`
command again. If the conclusion is `failure`, inspect the failing step before
going further:

```powershell
gh run view <RUN_ID> --log-failed
```

### 3. Get the artifact link

Substitute the `RUN_ID` you got in step 2:

```powershell
$runId = <RUN_ID>
gh api repos/nathanieluriri/opencluely-overlay-test/actions/runs/$runId/artifacts --jq '.artifacts[] | {name, url: .archive_download_url}'
```

It returns something like:

```json
{ "name": "OverlayPoc", "url": "https://api.github.com/repos/nathanieluriri/opencluely-overlay-test/actions/artifacts/123456/zip" }
```

The human-facing download page for the user is:

```
https://github.com/nathanieluriri/opencluely-overlay-test/actions/runs/<RUN_ID>
```

Tell the user to click the artifact named `OverlayPoc`, unzip it, and run
`OverlayPoc.exe`.

## One-liner that does not swallow errors

```powershell
git add -A;
git commit -m "fix: describe the change here";
git push;
if ($LASTEXITCODE -ne 0) { throw "push failed" }
Start-Sleep -Seconds 35;
gh run list --repo nathanieluriri/opencluely-overlay-test --limit 1 --branch master
```

Then fetch the artifact with step 3 once the run shows `completed  success`.

## Rules for models

1. Confirm the repo and branch before pushing: `git remote -v` and `git status`.
2. Push is the build trigger — there is no local compile step needed here.
3. Wait for the run before claiming a build passed. `queued`/`in_progress` is
   **not** success.
4. Report the `gh run list` status and the artifact link to the user verbatim.
5. If the run fails, fix the code, push again, and re-poll; do not report an
   artifact until a run shows `completed  success`.
6. `--limit 1` returns only the newest run. If you pushed multiple times, make
   sure the returned run's commit message matches your push.
