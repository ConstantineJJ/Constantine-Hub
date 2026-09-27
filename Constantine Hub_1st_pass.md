# Constantine Hub — 1st Pass

**Status:** planning / architecture baseline  
**Repository:** `ConstantineJJ/Constantine-Hub`  
**Purpose:** зафиксировать порядок восстановления локальной MCP-инфраструктуры и не потерять контекст между сессиями.

---

## 1. Цель проекта

`Constantine Hub` — единое Windows-приложение для запуска, остановки, диагностики и расширения локальных MCP-мостов и связанных runtime-компонентов.

Hub должен объединять управление инфраструктурой, но **не сливать все движки и мосты в один монолитный runtime**. Blender, Godot, Local Files и будущие адаптеры остаются независимыми технически, а Hub управляет ими через общий контракт.

Целевой вид:

```text
Constantine Hub
│
├─ Local Files MCP
├─ Blender MCP
├─ Godot MCP
└─ + future adapters
```

`my-lab-4-exp` **не используется** как инфраструктурный репозиторий. Это только Godot-лаборатория/проект.

Старый `Stickmans_Duel` больше не является источником инфраструктуры и не рассматривается как зависимость.

---

## 2. Базовый архитектурный принцип

Один Hub, несколько независимых adapters:

```text
Constantine Hub Core
│
├─ process manager
├─ health checks
├─ tunnel manager
├─ logs
├─ config/profile manager
├─ secrets references
└─ adapter host
     │
     ├─ LocalFilesAdapter
     ├─ BlenderAdapter
     ├─ GodotAdapter
     └─ FutureAdapter...
```

Предварительный общий контракт адаптера:

```text
IHubAdapter
├─ Detect()
├─ Setup()
├─ Start()
├─ Stop()
├─ Restart()
├─ Doctor()
├─ GetStatus()
├─ GetLogs()
└─ GetCapabilities()
```

Каждый adapter отвечает только за свой runtime и не должен знать внутренности других adapters.

---

# PASS A — Local Files MCP

## 3. Почему Local Files MCP делаем первым

Это первый приоритет.

После его подключения ChatGPT сможет работать с разрешёнными локальными каталогами напрямую, что сильно упростит дальнейшую разработку `Constantine Hub`, перенос Blender MCP, восстановление Godot MCP и диагностику локальной инфраструктуры.

Первый запуск допускается через PowerShell. После появления Hub запуск и управление Local Files MCP должны выполняться из приложения.

---

## 4. Возможности Local Files MCP v1

### Read operations

- list directory;
- stat file/directory;
- read text file;
- read file range;
- search by filename;
- search text in files;
- recursive tree/list;
- metadata: size, mtime, type;
- existence check.

### Write operations

Пользователь явно разрешил не только чтение, но и изменение файлов.

Нужны:

- create directory;
- create file;
- overwrite file;
- patch/edit text file;
- append text;
- copy file/directory;
- move/rename file/directory;
- delete file;
- delete directory;
- optional atomic replace / backup-before-write.

### Важное ограничение

Local Files MCP **не должен предоставлять произвольный shell/PowerShell/CMD execution** в первой версии.

Файловый доступ и command execution должны оставаться разными capability-классами.

---

## 5. Security model Local Files MCP

Доступ только к разрешённым корням (`allowed_roots`).

Пример:

```text
E:\MyCreations
F:\My Lab
D:\Desktop
```

Требования:

- canonical path validation;
- блокировка выхода через `..`;
- защита от symlink/junction escape;
- запрет чтения/записи за пределами allowlist;
- отдельная политика destructive operations;
- лимит размера читаемых/записываемых файлов;
- UTF-8 text path first, binary support позже;
- нормальные ошибки вместо silent fail;
- audit log всех write/delete/move операций;
- API keys и secrets не хранятся в Git.

Рекомендуемый режим v1:

```text
read/create/edit/write/move/delete = enabled
shell execution = disabled
allowed roots = explicit
```

---

## 6. Первый bootstrap Local Files MCP

Первый запуск:

1. создать MCP server;
2. создать tunnel profile;
3. пользователь создаёт API key;
4. key хранится локально вне Git;
5. tunnel запускается через PowerShell;
6. проверить `/healthz` и `/readyz`;
7. подключить MCP plugin в ChatGPT;
8. выполнить реальные read/write/create/delete QA-тесты в отдельной test-папке;
9. только после PASS разрешить работу с рабочими каталогами.

После появления Hub:

```text
Constantine Hub
→ Local Files
→ Start
```

должен заменять ручной PowerShell startup.

---

## 7. Acceptance criteria Local Files MCP

PASS только если из ChatGPT реально работают:

- list directory;
- read file;
- search text;
- create new file;
- edit existing file;
- create directory;
- rename/move;
- delete test file;
- попытка выхода из allowed root блокируется;
- попытка path traversal блокируется;
- restart tunnel не ломает профиль;
- logs показывают destructive operations.

---

# PASS B — Blender MCP_Con stabilization

## 8. Текущее состояние Blender

Рабочие компоненты:

- Blender bridge: работает;
- MCP server: работает;
- skills/router: 8/8;
- tunnel profile: работает;
- ручной `tunnel-client run --profile blender-local`: работает;
- ChatGPT → Blender MCP: подтверждено рабочим вызовом.

Проблема находится в `Blender MCP_Con` lifecycle/process management.

Найденный regression case:

```text
GUI alive
+ tunnel-client process missing
+ health port free/dead
→ Start All
→ приложение ошибочно пишет "Cannot start another tunnel"
```

Правильное поведение:

```text
health dead
+ owned tunnel process absent
→ clear stale state
→ start tunnel-client
→ wait healthz
→ wait readyz
→ RUNNING
```

---

## 9. Что исправить в Blender MCP_Con

- убрать ложный state `tunnel already exists`;
- хранить PID собственного child process;
- отличать `Owned`, `External/Adopted`, `Stopped`, `Failed`;
- не делать глобальный kill всех `tunnel-client.exe`;
- watchdog health state;
- restart/recovery после crash;
- cold-start test;
- stale-PID test;
- occupied-port test;
- external-process adoption test;
- логировать причину transition состояния;
- сохранить `Save Log`;
- убрать зависимости от старых абсолютных путей после миграции.

Важно: текущий `Blender MCP_Con v0.5.2` рассматривается как **донор/прототип**, а не как конечное отдельное приложение.

---

# PASS C — Constantine Hub Core

## 10. Создание Hub skeleton

После Local Files MCP и понимания локальной файловой структуры начинаем основной Hub.

Первый UI:

```text
Constantine Hub

LOCAL FILES
● MCP Server
● Tunnel
● Allowed Roots
[ Start ] [ Stop ] [ Restart ] [ Doctor ] [ Logs ] [ Settings ]

BLENDER
● Blender
● Bridge
● MCP Server
● Tunnel
● Skills
[ Start ] [ Stop ] [ Restart ] [ Doctor ] [ Logs ]

GODOT
○ Not installed / not configured
[ Setup ]
```

Для `Local Files MCP` кнопка `Settings` обязательна уже в первом Hub UI. Окно должно управлять allowlist без ручного редактирования JSON:

- `Add Folder` через системный folder picker;
- `Remove` выбранного root;
- отображение canonical/resolved path до сохранения;
- отдельные флаги `Read`, `Write`, `Delete` для каждого root;
- предупреждение при широком destructive доступе;
- валидация duplicate/nested/reparse-root конфликтов;
- сохранение только в machine-local config (`%APPDATA%\\ConstantineHub\\local-files-mcp.json` или его будущую schema-versioned замену), не в Git;
- безопасное применение: сначала validate, затем reload/restart только Local Files adapter при необходимости.

Главный экран Hub показывает только summary (`N roots`, `RW`, `Delete enabled`), а полный список путей живёт в Settings.

---

## 11. Hub Core responsibilities

Hub core должен содержать общие компоненты:

```text
apps/constantine-hub/
├─ core/
│  ├─ process-management
│  ├─ health
│  ├─ logging
│  ├─ profiles
│  ├─ settings
│  ├─ secrets-references
│  └─ adapter-contract
├─ ui/
└─ adapters/
```

Общие функции:

- process ownership;
- PID tracking;
- graceful stop;
- force stop только своего процесса;
- health polling;
- startup timeout;
- restart policy;
- logs aggregation;
- configuration validation;
- profile discovery;
- per-adapter status;
- one-click Doctor;
- one-click Start All / Stop All;
- per-adapter Start/Stop.

---

# PASS D — Blender migration into Constantine Hub

## 12. Перенос Blender управления

Когда Hub core стабилен:

- перенести working lifecycle из Blender MCP_Con;
- исправить найденные lifecycle bugs;
- подключить Blender adapter к Hub;
- сохранить существующий рабочий MCP server;
- сохранить bridge `127.0.0.1:9876` пока нет причины менять;
- сохранить 8/8 skills routing;
- перевести paths на новую структуру;
- проверить ChatGPT → tunnel → MCP → Blender.

После полноценного PASS отдельный `Blender MCP_Con.exe` можно вывести из эксплуатации.

---

# PASS E — Godot MCP rebuild

## 13. Новый Godot MCP создаём независимо

Старый Godot MCP client/runtime считается утраченным.

Новый вариант должен с самого начала жить вне Godot-проекта.

Целевая структура:

```text
Constantine-Hub/
adapters/godot-mcp/
├─ mcp-server/
├─ editor-bridge/
├─ runtime-bridge/
├─ project-addon/
└─ adapter/
```

Project-side часть минимальна.

---

## 14. Godot project plugin installer

Hub должен сам уметь подключать Godot project.

Workflow:

```text
Select Godot Project
→ Validate project.godot
→ Install/Update MCP plugin
→ Configure editor bridge
→ Configure runtime bridge/autoload if needed
→ Verify ports
→ Save project registration
→ Start
```

Для первой лаборатории:

```text
F:\My Lab\my-lab-4-exp
```

используется только как **target Godot project**, не как место хранения инфраструктуры.

Предварительные bridge ports можно сохранить совместимыми со старой схемой:

```text
Editor Bridge  : 6262
Runtime Bridge : 6263
```

Но окончательно фиксировать после реализации/QA.

---

## 15. Godot acceptance tests

Hub/Godot adapter считается готовым только после реальных вызовов:

- `godot_status`;
- scene tree read;
- inspect node;
- create node;
- modify property;
- save scene;
- run project;
- get stdout/errors;
- runtime input;
- runtime screenshot/capture;
- stop project;
- restart bridge/tunnel;
- close/reopen Godot and reconnect.

---

# PASS F — Unified Constantine Hub

## 16. Финальное объединение первого этапа

После PASS A–E один EXE должен управлять минимум тремя adapters:

```text
Constantine Hub
│
├─ Local Files MCP
├─ Blender MCP
└─ Godot MCP
```

При этом каждый adapter:

- стартует независимо;
- падает независимо;
- имеет отдельный health state;
- имеет отдельный tunnel/profile;
- не должен ломать другие adapters;
- может быть отключён пользователем.

`Start All` — orchestration, а не запуск одного монолитного процесса.

---

# PASS G — Packaging and releases

## 17. GitHub build pipeline

После стабилизации core:

```text
GitHub source
→ CI tests
→ Windows build
→ artifact
→ release zip / installer
```

Цель:

```text
ConstantineHub-win-x64.zip
или
ConstantineHub-Setup.exe
```

Первоначально portable ZIP предпочтительнее installer: проще обновлять и диагностировать.

---

## 18. Update strategy

Hub должен в будущем позволять легко обновлять:

- сам Hub;
- adapter;
- project plugin;
- config schema;
- skill/router metadata.

Не делать auto-update в первом pass. Сначала manual update + version display.

---

# 19. Предлагаемая repository structure

```text
Constantine-Hub/
│
├─ README.md
├─ Constantine Hub_1st_pass.md
├─ Project-pulse.md
│
├─ apps/
│  └─ constantine-hub/
│     ├─ core/
│     ├─ ui/
│     └─ adapters/
│
├─ adapters/
│  ├─ local-files-mcp/
│  ├─ blender-mcp/
│  └─ godot-mcp/
│
├─ runtime/
│  ├─ tunnel/
│  ├─ profiles/
│  ├─ health/
│  └─ processes/
│
├─ tests/
├─ docs/
└─ .github/workflows/
```

---

# 20. Что остаётся в Tools_C

`Tools_C` и `Constantine Hub` не должны дублировать обязанности.

Предварительное разделение:

### Tools_C

- contracts;
- canonical skills;
- workflows;
- project profiles;
- QA gates;
- reusable engineering rules.

### Constantine Hub

- local runtime control;
- MCP adapters;
- process lifecycle;
- tunnel lifecycle;
- application integration;
- plugin installation;
- local logs/status/Doctor.

Если позже обнаружится сильная причина объединить репозитории — решение принимается отдельно. На первом pass репозитории оставляем раздельными.

---

# 21. Что нельзя хранить в Git

Не коммитить:

- OpenAI API keys;
- control-plane keys;
- secrets;
- user-specific auth tokens;
- generated local runtime state;
- PID files;
- machine-specific caches;
- private local paths, если они не нужны как документированный пример.

Допускаются:

- config templates;
- example profiles без secrets;
- adapter manifests;
- documentation;
- source code.

---

# 22. Когда отдавать задачу Codex

Codex использовать как усиление, а не как обязательную зависимость.

Отдавать ему сложные локальные проходы:

- большие refactor;
- многофайловая миграция;
- Windows build/debug loops;
- сложные integration tests;
- GitHub Actions debugging;
- массовое обновление adapters;
- глубокий review перед release.

Архитектурные решения, scope и acceptance criteria сначала фиксировать в документах/Project Pulse, затем отдавать implementation agent.

---

# 23. Порядок выполнения — коротко

```text
1. Local Files MCP
   ↓
2. Подключить его через PowerShell + API key
   ↓
3. Проверить полноценные read/write/create/edit/delete операции
   ↓
4. Исправить lifecycle Blender MCP_Con
   ↓
5. Создать Constantine Hub Core + GUI
   ↓
6. Перенести Blender управление в Hub
   ↓
7. Написать новый Godot MCP + project plugin installer
   ↓
8. Подключить my-lab-4-exp как Godot target project
   ↓
9. Объединить Local Files + Blender + Godot под одним Hub EXE
   ↓
10. CI / Windows build / release package
```

---

# 24. First action after this document

**Начать реализацию `Local Files MCP`.**

Первый technical milestone:

```text
Local Files MCP v0.1
- explicit allowed roots
- list/read/search
- create/write/edit
- move/rename
- delete
- path traversal protection
- audit log
- stdio MCP server
- secure tunnel profile
- manual PowerShell bootstrap
- end-to-end ChatGPT QA
```

После этого Local Files MCP становится первым adapter-кандидатом для `Constantine Hub`.

---

## Decision summary

- [x] Название приложения: `Constantine Hub`
- [x] Отдельный repository: `ConstantineJJ/Constantine-Hub`
- [x] `my-lab-4-exp` не используется для инфраструктуры
- [x] Первый adapter: `Local Files MCP`
- [x] Local Files MCP имеет read/write/create/edit/delete
- [x] Произвольный shell не входит в Local Files MCP v1
- [x] Первый startup допускается через PowerShell
- [x] Последующие startups должны идти через Hub
- [x] Blender MCP_Con исправляется и затем поглощается Hub
- [x] Godot MCP создаётся заново как независимая инфраструктура
- [x] Godot plugin устанавливается/обновляется через Hub
- [x] Сложные локальные проходы при необходимости отдаём Codex
