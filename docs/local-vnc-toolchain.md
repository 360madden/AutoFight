# Independent AutoFight VNC developer toolchain

Updated: November 6, 2026. Scope: Windows **host-side development diagnostics** only. This Python environment is not used by the .NET 10 AutoFight WPF application, the guest Reader, or the production telemetry transport.

## Why it exists

The host-global `C:\Python314\python.exe` does **not** currently have `vncdotool` or `Pillow`. Lab 2's read-only VNC capture previously worked using a Python development environment located under the separate RiftReader2 repository. AutoFight should not depend on that checkout even for routine diagnostics. A private, repo-owned virtual environment resolves the tooling dependency without changing RIFT, VMware, or RiftReader2 files.

The known working Python version is **3.14.6**; the pinned `scripts/requirements-vnc.txt` versions were observed in that environment on November 6. The pinned file includes transitive dependencies, so a reinstall does not silently select newer dependency versions. It does not vendor packages or guarantee offline installation.

## One-time setup on the physical Windows host

In an ordinary **PowerShell** window:

~~~powershell
Set-Location 'C:\work\AutoFight'
& 'C:\Python314\python.exe' -m venv '.\artifacts\local\venv-vnc'
& '.\artifacts\local\venv-vnc\Scripts\python.exe' -m pip install --disable-pip-version-check -r '.\scripts\requirements-vnc.txt'
& '.\artifacts\local\venv-vnc\Scripts\python.exe' -c 'from vncdotool import api, client; from PIL import Image; print("AutoFight VNC imports OK")'
~~~

The `artifacts/local/` folder is excluded by `.gitignore`. Package installation may require network access; if it fails, do not modify another repository's environment as a workaround. Keep Python/venv files out of Git and off both guest VMs unless separately justified.

## Actual host verification (November 6, 2026)

- Created isolated environment: `af-local-vnc-venv-create-20261106-01`, exit 0.
- Installed the exact 15 pinned packages: `af-local-vnc-venv-pip-install-20261106-01`, exit 0.
- Import smoke verified `vncdotool 1.4.2` and `Pillow 12.3.0`: `af-local-vnc-venv-import-smoke-20261106-01`, exit 0.
- Dependency consistency: `af-local-vnc-venv-pip-check-20261106-01`, exit 0, no broken requirements.
- Actual **Lab 2 read-only** capture with AutoFight-local interpreter: `af-local-vnc-venv-capture-smoke-20261106-01`, exit 0, capture ID `3d01e8db651b4215a09b954ac4a7d75a`. The screenshot and DENIED review template are ignored local artifacts. No keyboard, mouse, addon bind, RIFT reload, VM lifecycle or GuestReader change was attempted.

This proves the AutoFight-local Python VNC tooling works end-to-end for screenshot capture. It does **not** prove chat focus, addon load, current XYZ or any game input.

## Read-only Lab 2 capture example

Only when a fresh capture is actually needed, from `C:\work\AutoFight`:

~~~powershell
& '.\artifacts\local\venv-vnc\Scripts\python.exe' '.\scripts\lab2_chat_status.py' --capture --output '.\artifacts\local\lab2-chat-focus-review-01'
~~~

This `--capture` operation reads the guest display and creates a DENIED review template. It sends no game input. **Do not** follow it with `--execute` unless the corresponding screenshot, exact game/VM identity, and fresh review record have passed the separately prescribed single-action gate. The same helper also supports a distinct `--capture --open-chat` plan; neither proves the chat caret is focused. Avoid reusing a capture output directory.

For future bind helpers, the Reader nonce is private: never place it in shell arguments, Workbridge command text, stdout, screenshots, Git, or review files. A status/transport diagnostic is not evidence that in-game XYZ is available.

## Verification and maintenance

- Use the same venv Python executable for every VNC capture/review/test command; do not silently fall back to a global or RiftReader2 Python interpreter.
- After a Python update, create a **new** ignored venv and import-test before switching. Do not overwrite the active environment mid-operation.
- If `vncdotool` imports but a VNC session fails, inspect the exact console endpoint, VMware state, current game identity and remote desktop focus separately. Installation success is not game input success.
