# Releasing TaskNinja

Same pipeline as ClipNinja. One-time setup, then every release is
three commands.

## One-time setup

1. Create the public GitHub repo **motter/TaskNinja** (empty — no
   README/license/gitignore checkboxes).

2. Push this folder as its root (terminal opened IN the folder that
   contains TaskNinja.csproj):

   ```bash
   git init
   git add .
   git commit -m "TaskNinja v1.1.0"
   git branch -M main
   git remote add origin https://github.com/motter/TaskNinja.git
   git push -u origin main
   git tag v1.1.0
   git push --tags
   ```

3. Wait ~5 minutes (Actions tab → green check), then Releases →
   v1.1.0 → download **TaskNinja-win-x64.zip** (the ~65 MB asset, NOT
   the "Source code" links) → extract TaskNinja.exe to
   C:\Apps\TaskNinja\ → run it. That's the last manual install.

The app defaults its update repo to motter/TaskNinja, so no in-app
configuration is needed. Settings can point elsewhere for forks.

## Every release

1. Bump the version in TWO places (keep them matching):
   - `TaskNinja.csproj` → `<Version>1.1.1</Version>`
   - `App.xaml.cs` → `DisplayVersion = "1.1.1"`

2. Update `CHANGELOG.md`, commit.

3. Tag and push:

   ```bash
   git add -A && git commit -m "v1.1.1"
   git tag v1.1.1
   git push && git push --tags
   ```

GitHub Actions builds the exe, zips it as TaskNinja-win-x64.zip, and
attaches it to an auto-created Release. Running installs offer the
update via the startup hint, the tray menu, or Settings.

## Rules that keep the updater happy

- Tags must look like `v1.1.1` (the updater strips the `v` and parses
  the rest as a version).
- The release must carry the workflow's published-exe asset. Don't
  delete it; don't attach source zips (assets with "source" in the
  name are ignored by the updater, but why tempt fate).
- The tag's version must be GREATER than the running one or clients
  consider themselves up to date.
- Every self-update leaves `TaskNinja.exe.bak` next to the exe —
  instant rollback if a release ever misbehaves: delete the bad exe,
  rename the .bak.
