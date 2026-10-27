# risca-sdk

Инструменты для процессора RiscA: ассемблятор командной строки `rasm` и библиотека кодирования инструкций.

| Проект | Путь | Что это |
|---|---|---|
| `RiscA.Core` | `src/RiscA.Core/` | Библиотека: `RiscA.Core.ISA` (`Instruction`, `OpCode`) и `RiscA.Core.Asm` (`Assembler`, `Parser`, `Tokenizer`) |
| `RiscA.Assembler` | `src/RiscA.Assembler/` | CLI `rasm` |
| `RiscA.Core.Tests` | `tests/RiscA.Core.Tests/` | Тесты (xunit + FluentAssertions) |

Документация: `doc/RiscA.MD` — спецификация ISA, `doc/README.md` — кодирование инструкций. Пример программы: `samples/base.test.rasm`.

## Использование

```
rasm -i <file.rasm> [-o <out.bin>] [-v] [-vp] [-vi]
rasm --version
```

`-v` — подробно, `-vp` — совпадения парсера, `-vi` — дамп инструкций.

## Локальная сборка

### Требования

- **.NET 10 SDK** (10.0.4xx). Target framework — `net10.0`, понижать не нужно.
- Компилятор C для NativeAOT:
  - **Windows** — Visual Studio или Build Tools с workload *«Desktop development with C++»* (MSVC + Windows SDK). Для `win-arm64` нужен дополнительно компонент *«C++ ARM64 build tools»*.
  - **Linux** — `sudo apt install clang lld zlib1g-dev` (для zip-архива нужен `zip`, если его нет — `build.sh` пакует через `python3`).
  - **macOS** — Xcode Command Line Tools (`xcode-select --install`).

### Быстрая сборка нативного бинарника

```powershell
# Windows (PowerShell)
.\build.ps1                       # win-x64 + win-arm64
.\build.ps1 -Rid win-x64          # только нужный RID
.\build.ps1 -Rid win-x64 -Patch 42
```

```bash
# Linux / macOS (в том числе WSL2)
./build.sh                        # linux-x64 (на aarch64 — linux-arm64, на macOS — osx-arm64)
./build.sh linux-x64              # только нужный RID
PATCH=42 ./build.sh linux-x64     # переопределить Patch
```

Результат: `artifacts/<rid>/rasm[.exe]` — самодостаточный нативный бинарник, работающий без установленного .NET — и `artifacts/rasm-<версия>-<rid>.zip`.

Эквивалентная команда без скриптов:

```
dotnet publish src/RiscA.Assembler/RiscA.Assembler.csproj -c Release -r win-x64
```

### Обычная сборка и тесты

```
dotnet build RiscASDK.slnx -c Release
dotnet test  RiscASDK.slnx -c Release
dotnet run --project src/RiscA.Assembler -- -i samples/base.test.rasm -o out.bin
```

## Версионирование

Версия `0.9.3` — это `Major.Minor.Patch`:

- **Major** и **Minor** — вручную в `version.props` (`<MajorVersion>0</MajorVersion>`, `<MinorVersion>9</MinorVersion>`). Major — при несовместимых изменениях синтаксиса/формата `.bin`/`.lst`, Minor — при обычном развитии.
- **Patch** — автоматически: число коммитов в HEAD (`git rev-list --count HEAD`) минус `<PatchBase>`. `PatchBase` — это число коммитов на момент выпуска `Major.Minor.0`; его меняют вместе с Minor, чтобы Patch снова считался с нуля (с `PatchBase = 0` Patch равен общему числу коммитов). Patch подставляют `build.ps1`, `build.sh` и CI как `-p:PatchVersion=` (переопределение: `-Patch 42` / `PATCH=42`).
- `MajorVersion`/`MinorVersion`/`PatchVersion` из `version.props` подхватывает `Directory.Build.props`, откуда их берут все проекты — и `dotnet build`, и `dotnet test`, и `dotnet publish`.

В `InformationalVersion` дополнительно попадает SHA коммита (`+<sha>`), поэтому сборку можно опознать по коммиту. Проверка: `rasm --version` → `rasm 0.9.3`.

## Платформы (NativeAOT)

В `src/RiscA.Assembler/RiscA.Assembler.csproj` стоит `<PublishAot>true</PublishAot>`, поэтому `dotnet publish -r <rid>` даёт маленький (~2 МБ) нативный бинарник без зависимости от .NET (для сравнения: self-contained single-file — 70–100 МБ). Рефлексии в коде нет, предупреждений trim/AOT тоже нет.

| RID | Чем собирается |
|---|---|
| `win-x64` | с Windows (MSVC x64) |
| `win-arm64` | с Windows, нужен компонент *«C++ ARM64 build tools»* |
| `linux-x64` | с Linux x64 (clang/lld), в том числе в WSL2 или Docker |
| `linux-arm64` | с Linux aarch64 либо кросс-сборка с x64 Linux (см. ниже) |
| `osx-arm64` | с macOS на Apple Silicon |

NativeAOT **не кросс-компилирует между ОС**: `win-*` собирается только с Windows, `linux-*` — только с Linux, `osx-*` — только с macOS. Внутри Linux кросс x64 → aarch64 работает и проверен (бинарник запускается под `qemu`):

```bash
sudo apt install clang lld zlib1g-dev gcc-aarch64-linux-gnu libc6-dev-arm64-cross binutils-aarch64-linux-gnu
./build.sh linux-arm64
```

`build.sh` и `build.ps1` для `linux-arm64` сами добавляют `-p:LinkerFlavor=lld -p:ObjCopyName=aarch64-linux-gnu-objcopy`: `bfd`-линкер и `objcopy` хоста x64 объекты aarch64 не понимают. Вручную это выглядит так:

```bash
dotnet publish src/RiscA.Assembler/RiscA.Assembler.csproj -c Release -r linux-arm64 \
  -p:LinkerFlavor=lld -p:ObjCopyName=aarch64-linux-gnu-objcopy

# проверка без ARM-железа
sudo apt install qemu-user-static
qemu-aarch64-static -L /usr/aarch64-linux-gnu artifacts/linux-arm64/rasm --version
```

## Сборка Linux-бинарника из-под Windows (WSL2)

1. Поставить SDK в WSL (Ubuntu):
   ```bash
   curl -sSL https://dot.net/v1/dotnet-install.sh -o dotnet-install.sh
   ./dotnet-install.sh --channel 10.0            # ставится в ~/.dotnet
   echo 'export DOTNET_ROOT="$HOME/.dotnet"; export PATH="$DOTNET_ROOT:$PATH"' >> ~/.bashrc
   ```
2. Компилятор: `sudo apt install clang lld zlib1g-dev`.
3. Репозиторий клонировать **в файловую систему WSL** (`git clone … ~/risca`), а не собирать из `/mnt/c/...`: там нет бита исполнения, а сборки идут заметно медленнее.
4. ```bash
   cd ~/risca
   chmod +x build.sh
   ./build.sh linux-x64
   ./artifacts/linux-x64/rasm --version
   ```

## CI

`.github/workflows/release.yml` запускается на push в `main` и вручную (`workflow_dispatch`):

1. **version** — считает версию: Major и Minor из `version.props`, Patch = число коммитов (checkout с `fetch-depth: 0`, чтобы история была полной) минус `PatchBase`.
2. **test** — `dotnet test` на ubuntu.
3. **build** — матрица `win-x64`, `win-arm64`, `linux-x64`, `linux-arm64` (кросс с x64), `osx-arm64`; каждый RID складывает zip в артефакты.
4. **release** — собирает все zip и `SHA256SUMS.txt` и публикует GitHub Release с тегом `v<версия>`. Повторный запуск на том же коммите даёт ту же версию и просто перезаписывает ассеты.

Работает: проверено на `main`, релиз публикуется автоматически (все пять RID собираются на GitHub-раннерах, включая `win-arm64` и кросс-сборку `linux-arm64`).

Рабочая ветка — `main`, она же ветка по умолчанию (Settings → Branches → Default branch); `master` удалена.

## Visual Studio Code
