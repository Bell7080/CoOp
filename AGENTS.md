# CoOpHorror / CoOp

Read README.md and Docs/NETWORKING.md before changing the game.

- Editor is Unity 6000.3.29f1 LTS. Keep URP and the committed package lock.
- Target: Windows desktop, recommended 2 players, maximum 6, player-hosted cooperative horror.
- Development connectivity is Unity Multiplayer Services + Relay. Steam lobby/transport is a later provider. Do not replace NGO or mix in another gameplay networking stack without explaining the migration.
- Use Unity CLI and the installed Unity skills. Prefer the running Editor to editing scene/prefab YAML. Run tools/unity.ps1 from PowerShell to pin commands to this checkout.
- One checkout, branch and Editor per developer/agent. Do not drive another checkout's Editor. Explicitly pass --project-path when bypassing the wrapper.
- Keep networking, session discovery, voice transport and voice audibility separate. Never send microphone PCM using gameplay RPCs.
- Every authoritative gameplay value has one writer. Clients request actions; the host validates them. Local player movement in the development sandbox is a prototype, not anti-cheat.
- Preserve .meta files with their assets. Commit Assets, Packages and ProjectSettings; exclude Library, Temp, Logs, UserSettings, Builds and credentials.
- Do not edit Packages/manifest.json manually. Add packages with the live Pipeline package commands.
- Save the console cursor before work and check new errors after recompilation. Do not clear the console.
- Verify networking in separate processes. A host alone or two identities inside one process is not a multiplayer test.
- Do not claim Steam connectivity, microphone audio or host migration work before they are implemented and tested.
- Follow Docs/VERSIONING.md for every delivered game/tooling update: increment VERSION, add CHANGELOG.md and a dated Docs/Devlog entry, and apply/verify PlayerSettings.bundleVersion before building. Record what was tested and what was not.
- Before official release, versions must be 0.minor.patch. Never use 1.0.0 or a higher major version unless the user explicitly requests the official release. Use tools/update-version.ps1, which only supports patch/minor updates within major zero.
- The initial bootstrap is version 0.1.0. Do not bump versions per individual file save or compile; one delivered update gets one version and one complete log.
- Use a feature branch and review the diff. Never force-push, silently publish, or change Unity versions.
