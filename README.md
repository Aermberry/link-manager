# data_mover

A small project to manage Windows data-migration scripts (robocopy /MOVE + junction), starting with the OpenMausBot migration.

## Repo contents

- `scripts/migrate-openmausbot.ps1` — migrate OpenMausBot data to `F:\DevelopEnv\<应用数据目录>\.openmausbot` and create junctions so apps keep using the old paths.
- `docs/OpenMausBot迁移报告.md` — notes and lessons learned from the migration.

## Usage (OpenMausBot)

1. Exit OpenMausBot (including tray) and Magpie.
2. Right-click `scripts/migrate-openmausbot.ps1` → **Run with PowerShell**.

## Roadmap

- Turn the one-off script into a parameterized template (source/dest, companion path, lock scan options).
- Add a checklist + file-count verification mode.
