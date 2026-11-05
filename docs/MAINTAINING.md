# Maintainer notes

## Automatic builds

Two GitHub Actions workflows live in `.github/workflows/`:

| Workflow | Runs when | What it does |
| --- | --- | --- |
| `build.yml` (job **build**) | Every pull request to `main`, every push to `main` | Builds in Release mode (warnings are errors), publishes a self-contained build and builds a test installer. Both can be downloaded from the run's **Artifacts** section for 14 days |
| `release.yml` | A tag like `v1.2.0` is pushed | Builds with that version number and creates a GitHub Release containing the installer (`AdvCamera-1.2.0-setup-x64.exe`), the portable zip (`AdvCamera-1.2.0-win-x64.zip`) and auto-generated release notes |

The installer is made with [Inno Setup](https://jrsoftware.org/isinfo.php) from `installer/AdvCamera.iss`.
`installer/build-installer.ps1` runs it, and installs Inno Setup through Chocolatey on GitHub's build
machines. Never change the `AppId` in the `.iss` file: Windows uses it to recognise upgrades.

Dependabot (`.github/dependabot.yml`) opens a pull request once a month when NuGet packages or GitHub Actions
have updates.

## Protecting `main`

Set this up once, after the first push. It ensures every change goes through a pull request with a passing
build. The `build` check only appears in the list after the workflow has run at least once, so push first.

1. Repository → **Settings** → **Rules** → **Rulesets** → **New ruleset** → **New branch ruleset**.
2. **Ruleset name**: `Protect main`. **Enforcement status**: *Active*.
3. **Target branches** → **Add target** → *Include default branch*.
4. Turn on these rules:
   - **Restrict deletions**
   - **Block force pushes**
   - **Require a pull request before merging**
     - Required approvals: `1` (with a single maintainer, see the note below)
     - Dismiss stale pull request approvals when new commits are pushed
     - Require conversation resolution before merging
   - **Require status checks to pass**
     - Require branches to be up to date before merging
     - **Add checks** → `build`
5. **Bypass list**: leave it empty, so the rules apply to admins too. If you are the only maintainer,
   GitHub does not let you approve your own pull request. Either set required approvals to `0` (pull request
   and green build still required) or add yourself to the bypass list *for pull requests only*.
6. **Create**.

Also recommended under **Settings → General → Pull Requests**: allow only **squash merging** (keeps
history tidy) and enable **Automatically delete head branches**.

Under **Settings → Actions → General → Fork pull request workflows**, keep *Require approval for first-time
contributors* so workflows from strangers don't run without your OK.

## Making a release

1. Make sure `main` is green.
2. Tag and push:

   ```powershell
   git checkout main
   git pull
   git tag v1.0.0
   git push origin v1.0.0
   ```

3. The **Release** workflow (about 5 minutes) creates the release with the installer and the zip.
   Edit its notes on GitHub if you want.

If a release build fails, fix the problem through a pull request, then delete the tag and push it again:

```powershell
git push origin :refs/tags/v1.0.0   # delete the tag on GitHub
git tag -f v1.0.0 origin/main       # move it to the fixed commit
git push origin v1.0.0
```

Version numbers follow `MAJOR.MINOR.PATCH`. The tag sets the version shown in the file properties of
`AdvCamera.exe`.

## Code signing (optional)

Unsigned downloads trigger a Windows SmartScreen warning ("Windows protected your PC"). Users can click
*More info → Run anyway*. To remove the warning you need a code-signing certificate. Free signing for
open-source projects is available from [SignPath Foundation](https://signpath.org/), and paid options include
Azure Trusted Signing.
