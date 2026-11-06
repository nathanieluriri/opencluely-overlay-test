# Build & Release the Overlay POC

- Repo: https://github.com/nathanieluriri/opencluely-overlay-test
- Branch: `master`
- Workflow: `.github/workflows/build-overlay-poc.yml` (manual dispatch only)
- Release tag: `overlay-poc-<run_number>`
- Asset: `OverlayPoc.zip`

There is **no .NET SDK** installed on this machine. Do not run `dotnet build` or
`dotnet publish` locally — all builds run on GitHub Actions (`windows-latest`).

## Prerequisites

```bash
cd "C:\Users\Mr Dashi\Downloads\open_cluely"
git remote -v                       # origin should be the repo above
gh auth status                      # must be authenticated
# if not: gh auth login
```

## 1. Commit and push

```bash
git status
git add -A
git commit -m "your message"
git push origin master
```

## 2. Trigger the cloud build

```bash
gh workflow run build-overlay-poc.yml --ref master
sleep 5
gh run list --workflow=build-overlay-poc.yml --limit 1
```

The first column of the list output is the run id (`databaseId`).

## 3. Wait for the build

```bash
gh run watch <RUN_ID> --exit-status
```

Takes about 1–2 minutes. On failure:

```bash
gh run view <RUN_ID> --log-failed
```

## 4. Get the download link

```bash
gh release list --limit 1
```

The newest release is tagged `overlay-poc-<RUN_NUMBER>`. The direct zip link is:

```
https://github.com/nathanieluriri/opencluely-overlay-test/releases/download/overlay-poc-<RUN_NUMBER>/OverlayPoc.zip
```

Or query it directly:

```bash
gh release view overlay-poc-<RUN_NUMBER> --json url,assets \
  --jq '{page: .url, asset: .assets[0].url}'
```

`RUN_NUMBER` is the GitHub Actions run number used in the tag, not the run id.
Read it from `gh release list` or from the workflow summary.

## What the workflow does

1. Checks out `master`
2. Installs .NET 8
3. `dotnet publish overlay-poc/OverlayPoc.csproj -c Release -r win-x64 --self-contained true -o overlay-poc/out`
4. Writes `README.txt` into the output folder
5. Zips `overlay-poc/out/*` to `overlay-poc/OverlayPoc.zip`
6. Uploads the zip as the `overlay-poc-win-x64` artifact
7. Creates the prerelease `overlay-poc-<run_number>` with the zip attached

## Troubleshooting

- **Publish fails**: read `gh run view <RUN_ID> --log-failed`; fix the C# and push again.
- **Release step fails**: check that the tag does not already exist for that run number.
- **`gh` not authenticated**: `gh auth login` (HTTPS + browser) before dispatching.
- **No release appears**: the workflow only runs on `workflow_dispatch`; pushing alone
  does not build anything.
