---
applyTo: ".github/workflows/*.yml"
---

# Workflows

`build.yml` is reusable and does the work; `pr.yml` and `release.yml` decide the version and what
happens to the packages.

- **`verify` input** — default true, and pull requests are the only place the verification jobs
  (validate, sample builds, Release link check, the three e2e suites) run. Releases pass
  `verify: false` because the tagged commit was already verified on its pull request. Keep the input
  name and meaning as they are; every job that verifies carries `if: ${{ inputs.verify }}`.
- **Publish gates** — betas publish only when the pull request carries the `publish-beta` label, and
  only from a branch in this repository (a fork gets no OIDC token). Keep `labeled` in the trigger
  types so applying the label starts a run. nuget.org versions are permanent, so publishing stays
  opt-in.
- **Release guard** — `release.yml`'s guard job proves the tag is an ancestor of the default branch
  before anything is packed. Never weaken or bypass it: it is the only thing standing between a tag
  cut from an unmerged branch and a published release verified by nothing.
- **Attestation** — keep `actions/attest-build-provenance@v2` over `artifacts/*.nupkg` and
  `*.snupkg`, with `attestations: write`, so consumers can run `gh attestation verify`.
- **Trusted publishing** — OIDC plus `NuGet/login`, `id-token: write`, `environment: nuget.org`, and
  `NUGET_USER` as the only secret. Policies on nuget.org are scoped to a single workflow file, so
  `pr.yml` and `release.yml` each need their own; a mismatched environment fails with HTTP 401 and
  "Environment mismatch for policy". Authenticate immediately before the push — the key lasts an
  hour and each token is exchangeable once. Never add a long-lived API key.
- **Runners** — the Apple jobs pin `macos-15`, not `macos-latest`, and select Xcode with
  `.github/scripts/select-xcode.sh`, which globs `Xcode_26.0*.app` because the images carry patch
  releases. `net10.0-ios26.0` will not build against a newer Xcode.
- **Artifacts** — `pack-android` and `pack-apple` upload `nuget-packages-android` / `-apple`; the
  `validate` job merges them and uploads the combined `nuget-packages`. Jobs that run when
  `verify` is false must download the `nuget-packages-*` pattern instead, since `validate` never
  ran. Any job restoring the sample needs both sets: the metapackages pack on the Apple runner only.
- **Debug builds** — the smoke apps and the sample build Debug. The one Release build is the Android
  link check, which also needs `-p:TargetFrameworks=<tfm>` alongside `-f`, because restore walks
  every target framework and the Apple legs fail with NETSDK1178 on Linux.
- **Live checks** — Android e2e runs the community server as a service container and publishes to
  `ws://10.0.2.2:5080/LiveApp/websocket`. Never switch it to `WebRTCAppEE`. The live job is
  `continue-on-error: true` deliberately; the offline suites are not.
- **auto-release / upstream-drift** — merging `docs/release-notes/<version>.md` tags the merge and
  dispatches `release.yml` (a tag pushed with `GITHUB_TOKEN` does not trigger `on: push: tags`,
  which is why the dispatch and the `workflow_dispatch` trigger both exist). Drift reports stay one
  issue per platform, fingerprinted so a standing drift is silent.
