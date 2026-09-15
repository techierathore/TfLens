# TfLens — TrBlazeUI 2.0.6 upgrade and deploy fix

| | |
|---|---|
| App | TfLens |
| Date | 2026-09-14 |
| Command | `*fix-issues` |

## In one paragraph

TfLens now uses TrBlazeUI **2.0.6**, the newest release, and the deploy workflow's image build works
again. The deploy broke because TfLens used a test build of TrBlazeUI (`2.1.0-ci.10`) that exists
only on the private package feed, and the Docker image downloads packages from nuget.org alone.
2.0.6 is on nuget.org and is byte-for-byte the same as the private feed's 2.0.6, so pinning it fixed
the deploy without adding any token to the pipeline. The three library problems TR-036, TR-037 and
TR-038 are fixed upstream in 2.0.6 and now closed here. The re-check is the smoke script in
`tests/.artifacts/trblazeui-206-smoke/` run against the published app, plus the app log it produced.

## 1. The failing deploy (run 34754269078)

**What failed.** The "Build image → GHCR" job, at the step `dotnet restore src/TfLens/TfLens.csproj`,
exit code 1.

**Why.** Reproduced locally with the same Dockerfile:

```
error NU1102: Unable to find package TrBlazeUI.Components with version (>= 2.1.0-ci.10)
error NU1102:   - Found 3 version(s) in nuget.org [ Nearest version: 2.0.6 ]
```

The Dockerfile does not copy `nuget.config`, so the restore inside the image only sees nuget.org.
The commit before the failure moved TfLens to `2.1.0-ci.10`, which is on the private GitHub feed
only.

**What changed.**

| File | Change |
|---|---|
| `src/TfLens/TfLens.csproj` | Both TrBlazeUI packages `2.1.0-ci.10` → `2.0.6` |
| `nuget.config` | Private feed removed. Listed without a token it fails restore with a 401 on any machine that has no token, even when every package is on nuget.org. How to add it back for a local trial is in the file. |
| `Dockerfile` | A comment only: the image restores from nuget.org, so every package version must be there. |
| `.github/workflows/deploy.yml` | No change. |

**Checked.** `docker build` of the full image with the fixed files: success. The solution build:
PASS, 0 warnings. The deploy itself runs on your next push to `main`. Nothing was pushed from here.

**Not changed, worth knowing.** The run also warns that `actions/checkout@v4` and the three docker
actions target Node.js 20, which GitHub is retiring. It is a warning, not the failure, and was left
alone.

## 2. Which release is "latest"

| Version | Where | Published (UTC) |
|---|---|---|
| **2.0.6** | nuget.org and the private feed, identical files | 2026-09-13 18:18 |
| 2.1.0-ci.12 | private feed only | 2026-09-13 15:22 |
| 2.1.0-ci.10 | private feed only (what TfLens used) | 2026-09-12 11:53 |

2.0.6 is the newest. Its reference document and XML documentation are identical to 2.1.0-ci.12's.
The package was read with the credentials in your Windows NuGet config. No credential was copied into
the repository.

## 3. The three open library problems

| Entry | What it was | Fixed in 2.0.6? | What TfLens changed | Measured (1280 and 390) |
|---|---|---|---|---|
| **TR-036** | Leaving a page with a Select logged an unhandled error | Yes, closed | Nothing to remove | 0 circuit errors and 0 Select errors in the log after leaving `/misses`, `/prices`, `/effort`. On ci.10: 299 and 1,396. |
| **TR-037** | The short chart form drew no value labels | Yes, closed | Three charts went back to the short form (`Harness.razor`, `Routing.razor`, `PlaybookModelTokens.razor`) | Harness: 3 bars, 3 labels. Routing: 5 bars, 5 labels. |
| **TR-038** | A truncated Outline badge lost its text colour | Yes, closed | Three extra CSS lines removed from `ShellHeader.razor.css`; stale warnings removed from `SyncNowButton.razor` | The header badge keeps `text-foreground` and still truncates with an ellipsis. |

No console errors and no sideways scrolling on any screen checked.

**Not proven on screen:** the Playbook tokens chart. It uses the same chart form as the two proven
ones, and it builds, but the demo account has 0 Playbook repositories, so it has nothing to draw.

## 4. One new library problem filed

| Entry | What it affects | Blocks or breaks anything? |
|---|---|---|
| **TR-039** — the chart library TrBlazeUI uses logs an error when a page with a chart is left | The server log only: six error lines in this smoke, all from the chart library's own clean-up. It was already happening on ci.10. | No. Nothing the user sees. |

The prompt that fixes it, for the TrBlazeUI repository:

```
Fix TR-039 from docs/TfLens-TrBlazeUI-Feedback.md in the TfLens repo: ApexChart's Dispose
leaves an unobserved JSDisconnectedException when a Blazor Server circuit ends.
```

## 5. Screen check notes

The framework's screen check marked `/routing` and `/` as failed. Neither failure comes from this
change:

- `/routing`: the four missing controls (`model-tokens`, `repricing-actual`, `repricing-max`,
  `edit-prices`) are in tabs the check does not open. The chart in the Models tab was measured
  directly instead, with its labels, as above.
- `/`: the missing `repo-streams-blog` and `repo-streams-AI-First-Playbook` are named after
  repositories that are not in the demo account.

## 6. Evidence on disk

- Smoke script and measurements: `tests/.artifacts/trblazeui-206-smoke/`
- Screen check: `tests/.artifacts/verify/screens.json`
- App log of the smoke: `tests/.artifacts/verify/app-5014.log`
- Closing lines: `docs/TfLens-TrBlazeUI-Feedback.md`, where TR-036, TR-037 and TR-038 are recorded
  as closed, each re-checked by the smoke script above
