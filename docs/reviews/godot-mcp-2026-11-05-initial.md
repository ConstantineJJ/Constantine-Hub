# Godot MCP v0.5.5 — первоначальное ревью до исправлений

Дата: 2026-11-05. База: tag v0.5.5, commit accae36236f304e209501a082dd3d6d262577742.
Репозиторий: E:/MyCreations/Constantine-Hub. До начала работы присутствовали
незакоммиченные изменения NVIDIA/RTX, анимации, smoke runner и workflow;
они не относятся к расследованию и сохраняются отдельно от коммитов задачи.
Исходный smoke suite прошёл (artifacts/godot-review-baseline-smoke.txt).
Этот отчёт зафиксирован до изменения рабочего кода и тестов.

## Источники и архитектура

Полностью прочитан экспорт D:/Desktop/Constantine-Hub-log-2026-11-05_153911.md:
688 строк. Повторяющиеся JSON bootstrap-события разобраны по полям, стек исключения
прочитан полностью. Лог не содержит снимков UI или /api/status в момент аварии,
версий зависимостей на момент аварии, HTTP 502, состояния паузы игрового процесса
или содержимого JSON-RPC-ответа, вызвавшего сообщение о неизвестном ID.

ManagedProcess запускает tunnel-client без окна, читает stdout/stderr асинхронно.
Туннель запускает run_godot_mcp.cmd → venv Python launcher → Python.
Read-only инвентаризация текущей установки: tunnel PID 16912 → cmd PID 20052 →
venv Python PID 4356 → Python PID 20388. /api/status рекламирует именно 20052.
Это наблюдение после восстановления, не снимок инцидента.

TunnelRuntimeProbe.ProbeAsync проверяет TCP health-порт, /readyz, /api/status,
идентичность туннеля/адреса/команды, main enabled, probe_status и существование PID.
TunnelProfileAdapterBase отображает McpUnavailable/Occupied как Degraded;
RunningExpected как Running (owned) либо External. Уже существуют NotConfigured,
Stopped, Starting, Running, External, Degraded, Failed. Замена enum не нужна.

GodotAdapter сериализует Start/Stop/Restart/Doctor/Recover/SelectProject одним
SemaphoreSlim. Start проверяет skills/plugin/profile и ждёт туннель до 12 секунд.
Recover имеет предел 90 секунд; Legacy вызывает Restart, ALT — Stop/Doctor/Start.
Doctor/setup используют ProcessCapture и убивают своё дерево при отмене.
Внешние туннели защищены от Start-repair/Restart/смены проекта.

Godot editor bridge (6262) и runtime bridge (6263) — отдельные TCP JSON-line
серверы. Runtime обслуживается Node._process игрового процесса; он не обязан
работать при остановленной игре. Статусы проверяют идентичность проекта.
Python использует новый сокет на запрос, request ID, ограничения размера и общий
тайм-аут; ошибка инструмента превращается в ToolError, не требует рестарта.
SDK сериализует stdout одним writer task; ConHub напрямую эти потоки не закрывает.

## Connection Holder и ReConnect

Connection Holder/аналогичного постоянно включённого автоматического восстановления
в исходниках нет. Периодическая диагностика — не восстановление. ReConnect opt-in,
по умолчанию выключен, состояние не сохраняется между запусками приложения.
Таймер UI: 5 секунд, при скрытом окне 15 секунд. Fallback имеет свой single-flight
guard, подтверждение отказа через 5 секунд, 9 попыток (Restart, Restart, ALT × 3),
10 секунд между попытками; после исчерпания Waiting до ручного rearm.
Критическое сообщение само по себе не вызывает рестарт при здоровой проверке.
Новый Connection Holder в рамках задачи запрещён и не планируется.

## Findings

| ID / серьёзность | Симптом, причина и доказательство | Функция | Влияние, рекомендация, риск |
|---|---|---|---|
| F1 / P1 | Явная смерть stdio не входит в статус; PID оболочки и кэш /readyz/probe_status недостаточны для доказательства MCP. Код уже ловит отсутствующий PID, поэтому гипотеза о проверке только tunnel-client неверна. Ложный зелёный в самом инциденте не доказан. | TunnelRuntimeProbe.ProbeAsync; TunnelProfileAdapterBase.GetStatusAsync/StartAsync; ReconnectFallback.ProbeAsync | Дополнить существующую диагностику точным terminal exit event текущего owned процесса. Не использовать широкие строки ошибок инструментов. Сброс только при новом процессе, защита от запоздалого события предыдущего процесса. Shared transport затрагивает остальные MCP; внешние/исторические события и зависший живой MCP остаются ограничением. |
| F2 / P1 | Kill exception или WaitForExit(false) всё равно приводит к Dispose/null в finally, вызывающий код сообщает об остановке и может продолжить рестарт. Подтверждено кодом; в логе инцидента остановка успешна. | ManagedProcess.Stop; TunnelProfileAdapterBase.StopOwnedTunnelAsync | Сохранить владение при неподтверждённой остановке и вернуть ошибку, не разрешая следующий Start. Общий компонент всех managed MCP. Риск — более явные ошибки остановки; проверить контролируемые отказы. |
| F3 / P1 | Явный пользовательский Stop не отключает ReConnect: таймер после Stop вновь запускает Godot. | MainForm.StopAllOwnedAsync, MakeAdapterActions, WorkspaceFeatures.StopBridgeAsync, ReconnectUi.InitializeReconnect | Отменить fallback перед каждым пользовательским Stop Godot через общий dispatcher. Не менять внутренний Stop в Legacy/ALT. Риск — пропустить UI-путь; тестировать dispatcher и активную отмену. |
| F4 / P2 | Любая недоступность 6263 в UI становится Idle. Snapshot.RuntimeBridgeIssue отбрасывается. TimeoutError с пустым str даёт `failed: `, как строки 390 и 669 лога. | MainForm.RefreshStatusAsync; WorkspaceFeatures.UpdateBridgeRail; GodotBridgeProbe.ProbeAsync; JsonLineBridge._acall | Показывать причину, различать timeout/refused/invalid response/other project, указать что runtime необязателен. Только Godot. Не выдавать отсутствие listener за доказанную остановку игры. |
| F5 / P2 | Сообщение об общем восстановлении означает только editor bridge + tunnel status; активного MCP roundtrip и обязательной проверки runtime нет. | ReconnectUi.InitializeReconnect; ReconnectFallback.Restored; GodotAdapter.RecoverAsync | Сохранить критерий editor/tunnel и алгоритм, уточнить объём проверок и отдельный результат runtime. Не делать runtime обязательным: иначе будут рестарты при нормальной остановке игры. Не утверждать доказанную end-to-end готовность MCP. |

Дополнительные границы: polling snapshot и lifecycle могут перекрываться;
snapshot использует изменяемый _settings через await. При изменении этого участка
следует захватывать локальные settings. Runtime bridge poll зависит от _process;
пауза SceneTree может прекратить обслуживание. Пауза в инциденте не доказана,
изменять поведение игровых операций во время паузы без отдельной проверки нельзя.

## Реальный инцидент и гипотезы

13:30:47 (стр. 6) — запущен tunnel PID 17212, не Python PID.
13:50:43 (365) — Node not found; MCP продолжает обрабатывать запросы.
13:54:01 (390) — runtime status failure с пустым описанием, ещё до смерти MCP.
13:54:45 (408–480) — invalid response ID, closed pipe и traceback stdout.flush;
OSError(22) выходит из task group, Python завершается с кодом 1.
13:54:46 (481) — tunnel явно остаётся в degraded mode; 484–491 — closed channels
до 14:38:47. Stop ConHub в это время отсутствует.
14:53:10–20 (492–655) — ручное включение ReConnect, подтверждение, одна Legacy
попытка, подтверждённая остановка PID 17212, новый tunnel PID 16912, успех проверки.
14:55:46–52 (656–657) — transient sample проходит подтверждение без рестарта.
15:16:07 (669) — runtime call_method timeout; нет второго traceback или terminal exit.

A: частично. PID оболочки не доказывает MCP, но проверка уже гораздо шире PID туннеля.
B: частично. McpUnavailable/Degraded существуют; явный terminal event не учитывается.
C: формулировка успеха чрезмерна; восстановление Python не опровергается поздним runtime timeout.
D: непосредственный механизм выхода — необработанный stdout.flush OSError(22).
Первичный инициатор нарушения pipe не установлен: события разных процессов имеют
одинаковую секунду. Нет доказательств принудительной остановки, повреждения JSON
прикладным print или несовместимости Python 3.14. Не подавлять ошибку и не повторять
запись частично отправленного JSON в закрытый канал.
E: runtime — отдельная зависимость, сбой предшествует аварии и повторяется после
рестарта MCP. Причина недоступности самого Godot (пауза/нагрузка/остановка/иное)
не установлена. Работоспособность редактора и игрового процесса различаются.

Текущая установленная среда: Python 3.14.2, MCP 2.2.0, AnyIO 4.15.1. Это не
доказательство неизменности среды с момента аварии. Обе зависимости декларируют
поддержку Python 3.14: https://raw.githubusercontent.com/modelcontextprotocol/python-sdk/v2.2.0/pyproject.toml
и https://raw.githubusercontent.com/agronholm/anyio/4.15.1/pyproject.toml .
SDK stdio.py прочитан из установленного venv: fd claims/private duplicates,
один stdout writer, flush через worker thread, восстановление fd в finally.
Python bridge закрывает только свой disposable TCP writer, не stdio SDK.

## Решение до реализации

Исправлять F1–F5 минимально, сначала воспроизводящие тесты; оставить текущую модель
состояний и план девяти попыток. Уточнить причины bridge errors, не чинить
неустановленную первопричину EINVAL. Контролируемые отказы только на mocks/fixture.
Не запускать искусственных аварий live Godot, не менять dist/установку, не публиковать
релиз. Кандидат v0.5.6 возможен после сборки, smoke, Python tests и проверки упаковки;
реальная длительная интеграция и повтор инцидента останутся отдельной приёмкой.
