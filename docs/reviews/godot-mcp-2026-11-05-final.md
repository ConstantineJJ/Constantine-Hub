# ConHub v0.5.6-rc.1 — итог расследования Godot MCP

Дата: 2026-11-05. Ветка: `fix/godot-mcp-v056`.
Исходная версия: v0.5.5 / `accae36236f304e209501a082dd3d6d262577742`.
Код собранного кандидата: `2645f9a233c038ed8e4c24330f5f01ffd22b0ede`.
Релиз не опубликован, push и замена действующей установки не выполнялись.

## 1. Первоначальное ревью и уточнение требований

[Первоначальный самостоятельный отчёт](godot-mcp-2026-11-05-initial.md) представлен
перед изменением исходников; его первый Git-коммит — `6f48d9b`. Он содержит
архитектуру, жизненный цикл, первоначальные findings, доказательства, влияние на
другие MCP, рекомендации и риски. [Дополнение о транспорте](godot-mcp-2026-11-05-transport-addendum.md)
отдельно фиксирует причину, установленную позднее контролируемым воспроизведением.

Полностью изучен `D:/Desktop/Constantine-Hub-log-2026-11-05_153911.md` (688 строк).
Изучены исходники ConHub v0.5.5, текущие изменения, установленный Python MCP/AnyIO,
обе локальные копии исходников tunnel-client и сам установленный binary.

**Connection Holder отсутствует.** Есть периодическая диагностика 5/15 секунд и
отдельный opt-in ReConnect, выключенный по умолчанию. Постоянно включённого
автоматического восстановления нет. Новая реализация Connection Holder не создана.

Первоначальный F3 «Stop конфликтует с ReConnect» **отозван после уточнения
пользователя**. В итоговом коде нажатие Stop не выключает ReConnect: включённый
ReConnect должен восстановить соединение. Для окончательной остановки нужно сначала
вручную выключить ReConnect. Промежуточное auto-disable удалено отдельным коммитом;
история сохранена. Итоговое отличие Stop-путей от v0.5.5 отсутствует.

## 2. Подтверждённые причины и гипотезы

### Авария stdio: установлен дефект распределения ответов tunnel-client

Установленный binary `C:/Users/kosti/chatgpt-blender-mcp/tunnel-client.exe`:

- версия из изолированного `/api/status`: `0.0.9+62b9b42f698ec5319d2115e0c0ff1dcf6557d7ae`;
- SHA-256: `6cf9b0ba8f01a661bb040cbf5223c725beef51e3aa6fe6ebe08fb9d364f8334a`.

Параллельные обработчики main stdio читают общий поток. Ответ с корректным ID
другого запроса попадает не тому обработчику. Туннель пишет `Received response
without valid ID`, закрывает общее соединение, и последующая запись/flush Python
получает `OSError(22)`. После выхода дочернего MCP туннель продолжает работать,
`/readyz` остаётся HTTP200 и `probe_status=ok`; запросы получают 502.

Локальные исходники подтверждают механизм: `pkg/mcpclient/shared_connection_transport.go`,
`sharedConnection.Read/Close` (66–87); `pkg/dispatcher/internal/processor.go`,
`mcpProcessor.forwardResponses` (666–687, 780–783); `pkg/dispatcher/fxmodule.go`
(181–184): сериализация только harpoon, без неё для main. Корень этих исходников:
`C:/Users/kosti/chatgpt-blender-mcp/tunnel-client-0.0.9-context-conduit-topaz`.
Архивы исходников не имеют Git-метаданных; точное соответствие их сборке не
установлено. Вывод подтверждён прямой проверкой установленного binary.

| Изолированный запуск установленного binary | Наблюдение |
|---|---|
| Штатные active workers | Валидный ID попадает другому запросу; closed pipes, Python errno22, stdio exit, 502; `/readyz` зелёный |
| Только `--control-plane.max-inflight 1` | Тот же сбой: параметр ограничивает очередь, а не active workers |
| `--mcp.max-concurrent-requests 1` | 8/8 ответов HTTP200, правильные ID, без закрытия pipe, errno22 и degraded exit |

Использовались только loopback control plane, fake stdio Python, пустые приватные
профили/конфигурация, dummy key и динамические порты. Godot и реальные туннели в
этих аварийных тестах не участвовали. Все созданные процессы ограничены по времени.
Повторяемый тест сохранён в `adapters/godot-mcp/tests/stdio_tunnel_concurrency.py`.

Это воспроизводит механизм и порядок событий production-инцидента. Точный первый
неверно распределённый production-пакет восстановить нельзя: исходный лог не
содержит DEBUG-корреляции/самих RPC-ответов. Поэтому первопричина класса отказа
подтверждена, а конкретное историческое совпадение ID остаётся наиболее вероятным
объяснением, не побайтным forensic-доказательством.

### Остальные гипотезы

| Гипотеза | Итог |
|---|---|
| A: проверяется только tunnel-client | Опровергнута в такой формулировке. Уже были health/identity/channel/PID-проверки. Частично подтверждён недостаток: рекламируемый PID — `cmd.exe`, а его существование и кэшированные статусы не доказывают MCP roundtrip. Добавлено терминальное свидетельство текущего owned процесса. |
| B: нет обработки DEGRADED | Опровергнуто как отсутствие состояния: `McpUnavailable → Degraded` уже было. Подтверждены stale-ready upstream и отсутствие привязки явного stdio exit к статусу ConHub. |
| C: восстановление проверяется неполно | Критерий — tunnel checks + editor project identity, без обязательного runtime и без активного MCP roundtrip. Это сохранено, формулировка успеха уточнена. Поздняя ошибка runtime не опровергает восстановление Python. |
| D: виноват Python 3.14 | Не подтверждено. Ошибка errno22 воспроизведена после закрытия peer pipe даже без MCP SDK. Установленные MCP 2.2.0 и AnyIO 4.15.1 декларируют Python 3.14; тесты на Python 3.14.2 прошли. |
| E: отдельная проблема Runtime Bridge | Подтверждено разделение компонентов. Тайм-аут 6263 был до аварии и повторился после восстановления MCP. Причина недоступности самого игрового runtime не установлена. |

Поддержка Python сверена с первичными metadata:
[MCP 2.2.0](https://raw.githubusercontent.com/modelcontextprotocol/python-sdk/v2.2.0/pyproject.toml),
[AnyIO 4.15.1](https://raw.githubusercontent.com/agronholm/anyio/4.15.1/pyproject.toml).
Это не доказательство отсутствия любых ошибок Python/SDK; менять их без причины не требуется.

`Node not found: MortarAmmoPickups` — локальная ошибка инструмента, после неё запросы
продолжались. Связь с аварией не доказана. Runtime bridge обслуживается игровым
`Node._process`, editor bridge — редактором; остановка/пауза/блокировка runtime не
равны смерти MCP. В частности, пауза SceneTree может остановить polling, но состояние
паузы в инциденте неизвестно. Принудительного Stop ConHub в 13:54 в логе нет.

## 3. Исправленные дефекты и их риски

| Изменение | Обоснование / область | Ограничение или риск |
|---|---|---|
| Один active MCP worker при запуске Godot | `GodotAdapter.TunnelRunArguments`, `TunnelProfileAdapterBase.StartAsync`; прямое binary-воспроизведение доказывает результат. Legacy и ALT используют тот же Start. | Последовательные запросы: медленный инструмент задержит очередь. Внешние launchers не изменяются. Другие MCP сохраняют прежние параметры. |
| Явный terminal stdio exit влияет на readiness | `TunnelProfileAdapterBase.ObserveTunnelLog/ProbeRuntimeAsync`; точный structured event mcpclient/stdio текущего owned процесса. | Не заменяет MCP ping; не обнаруживает произвольное зависание или пропущенную историческую/внешнюю аварию. |
| Изоляция поколений процессов | `ManagedProcess.ForwardOutput/Started`: reset и forwarding под одним gate. | Запоздалые callbacks старого запуска не могут пометить новый degraded. Потоки не переиспользуются. |
| Не терять владение после неудачного Stop | `ManagedProcess.Stop`: kill exception или неподтверждённый выход возвращают ошибку и сохраняют handle; замена не запускается. Dispose продолжает cleanup других адаптеров. | Реальный OS denial нельзя устранить кодом; остаётся возможность повторного Stop. Общий компонент проверен на timeout/denial mocks и отдельных процессах. |
| Проверять отмену до запуска | `TunnelProfileAdapterBase.StartAsync`, `TunnelRuntimeProbe.ProbeAsync`: до операции, после await/probe и перед созданием процесса. | Отмена кооперативная: не откатывает уже выполненную удалённую команду или создание процесса, случившееся до проверки отмены. |
| Сохранять конкретные bridge errors | `GodotBridgeProbe`, `JsonLineBridge._acall`, `GodotAdapter.GetStatusAsync`, MainForm и rail. | За 800 ms Windows может дать connect timeout вместо ConnectionRefused; теперь он отличается от timeout уже установленного status-соединения. Отсутствующий runtime остаётся необязательным. |
| Уточнить сообщение ReConnect | `ReconnectFallback.Restored`, `InitializeReconnect`: перечисляет реально пройденные checks и отдельный runtime result. | Успех не означает гарантированную доступность произвольного инструмента или игрового runtime. Новых polling-запросов нет. |
| Единый settings snapshot | `GodotAdapter.GetSnapshotAsync` захватывает локальные settings до await. | Устраняет смешивание полей старого/нового проекта внутри snapshot; полная сериализация status с жизненным циклом не добавлялась. |

Модель `NotConfigured/Stopped/Starting/Running/External/Degraded/Failed` сохранена.
Значения ready/starting/degraded продолжают поступать из существующей диагностики;
имена CONNECTED/RECOVERING отдельными новыми enum не вводились.

## 4. ReConnect после исправлений

Схема сохранена: **Restart, Restart, ALT Stop → Doctor → Start**, три цикла,
максимум девять попыток. Подтверждение двумя проверками через пять секунд,
ожидание десять секунд между попытками, Waiting после исчерпания, ручной rearm.
Единичная ошибка инструмента/единичный пропуск не требуют рестарта.

Ручное выключение ReConnect проверено во время активной попытки и во время
запланированных 5/10-секундных ожиданий. Даже продолжение, намеренно игнорирующее
отмену до завершения ожидания, не вызывает новых probes/retries/restored-log.
Повторные timer checks после выключения не включают механизм сами. Включённый
ReConnect восстанавливает вручную остановленное соединение, как требует архитектура.
Алгоритм Enable/Disable и последовательность попыток не переписывались.

## 5. Результаты проверок

Все перечисленные результаты относятся к контролируемым fixtures, если явно не
указан иной уровень. Live аварии текущего Godot не устраивались.

| № | Сценарий | Результат и доказательство |
|---|---|---|
| 1 | Штатное подключение | PASS: owned/external health lifecycle, корректная identity, повторный Start без дубликата; binary one-worker 8/8 |
| 2 | Единичная ошибка инструмента | PASS: Node-not-found не critical; ошибочный инструмент/бюджет ответа не мешают последующим stdio calls |
| 3 | Краткий пропуск | PASS: повторная проверка возвращает Observing без recover |
| 4 | Смерть дочернего MCP | PASS на mocked dead PID и terminal event; реальный изолированный Python также завершается после закрытия pipe в upstream-воспроизведении |
| 5 | Туннель жив, MCP недоступен | PASS: terminal event overrides живой wrapper и stale-green; до замены не становится Running; старое событие не портит новый процесс |
| 6 | Runtime Bridge недоступен | PASS: closed listener/connect timeout отдельно от accepted-but-no-status timeout, wrong project, malformed/oversized/cancelled TCP frames; runtime не вызывает лишний restart |
| 7 | Legacy Restart | PASS: owned процесс заменяется, readiness достигается; fallback прекращает попытки после успеха. Полного live Godot recovery здесь не было. |
| 8 | Неудачные Legacy → ALT | PASS: точная последовательность двух Legacy и ALT в каждом из трёх циклов; Godot ALT Stop/Doctor/Start ordering проверен отдельно |
| 9 | Девять неудач | PASS: Waiting после 9, повторный timer tick не запускает новый цикл; только ручной rearm |
| 10 | Конкурентные события | PASS: single-flight fallback, общий Godot operation gate, cancel queued operations, failed Stop не создаёт дубликат, late callbacks изолированы |
| 11 | Blender / Local Files | PASS для существующих shared lifecycle/UI regressions и неизменённых launch arguments; Local Files 9/9. Реальная Blender-сессия не тестировалась. |

Дополнительные результаты:

- Финальные чистые исходники кандидата: **429** smoke/UI assertions, PASS.
- Рабочий checkout с сохранёнными RTX-изменениями: **548**, PASS.
- Godot Python: **8 тестов**, PASS без skipped, включая headless engine на отдельном
  синтетическом проекте. Действующий редактор/игра не использовались.
- Упакованный Python: **29 инструментов**, initialize/list/call, **40 concurrent**
  запросов, ordinary tool error и последующий успешный запрос, PASS.
- Local Files: **9 pytest tests**, PASS.
- Helper semantic backend: **30** assertions; protocol **66**, lifecycle **51**;
  stdio + streamable HTTP + 60 lightweight health probes, PASS.
- Сборка и portable publish Hub/Updater/Helper, PASS; .NET vulnerability audits
  Hub и Helper без диагностик/уязвимостей, PASS.
- Проверены **26** bundled Godot файлов по SHA-256 и шесть исходных animation PNG.
  Venv/cache/pyc отсутствуют в конечной упаковке. Helper manifest проверяется тестами.
- Официальный ZIP v0.5.5 сверён с SHA-256
  `2077558196c603ef1dcd91e0428413880d7bcb5c72bfe8720e1a04f9fcd9ca12`.
  На изолированной копии прошли locked-update protection, upgrade → 0.5.6.0,
  запуск опубликованного приложения с marker, реальный Helper probe, сохранение
  настроек и побайтный rollback к v0.5.5.

Основные receipts в `artifacts/godot-review/`: `candidate-smoke-final.txt`,
`final-working-smoke.txt`, `final-python.txt`, `candidate-protocol.txt`,
`local-files-tests.txt`, `helper-tests.txt`, `candidate-helper-protocol.txt`,
`candidate-helper-lifecycle.txt`, `upgrade-v055-final.txt`, `candidate-manifest.json`,
`tunnel-concurrency-final/`. Red-тесты до исправлений также сохранены.

GitHub CI и релиз не запускались. Не выполнялись полный live end-to-end Godot
recovery с текущим проектом и длительный эксплуатационный endurance. Они не PASS.

## 6. Изменённые файлы

От корня `E:/MyCreations/Constantine-Hub`:

- `apps/ConstantineHub/Core/{ManagedProcess,TunnelProfileAdapterBase,TunnelRuntimeProbe,ReconnectFallback}.cs`;
- `apps/ConstantineHub/Adapters/Godot/{GodotAdapter,GodotBridgeProbe}.cs`;
- `apps/ConstantineHub/{MainForm,ReconnectUi,WorkspaceFeatures}.cs`;
- `adapters/godot-mcp/src/godot_mcp/bridge_client.py`;
- `apps/ConstantineHub.SmokeTests/{GodotDiagnosticsTests,GodotRecoveryTests,ManagedProcessRecoveryTests,ReconnectTests,TunnelLifecycleTests}.cs`;
- `adapters/godot-mcp/tests/{smoke_mcp,stdio_fake_server,stdio_tunnel_concurrency,test_transport_limits}.py`;
- `apps/ConstantineHub/ConstantineHub.csproj`, `apps/ConstantineHub.Updater/ConstantineHub.Updater.csproj`;
- `README.md`, `CHANGELOG.md` и три отчёта в `docs/reviews/`.

Существовавшие незакоммиченные изменения NVIDIA/RTX, Program.cs и workflow
сохранены побайтно: проверено совпадение первоначального binary Git-diff и
оставшегося diff, а также хешей всех восьми первоначальных untracked файлов.
Они не включены в коммиты задачи или изолированную сборку кандидата.

## 7. Оставшиеся риски и отклонённые предложения

- Причина конкретного отказа Runtime Bridge неизвестна; смена портов, принудительное
  изменение паузы или перезапуск Godot редактора/игры не обоснованы и не выполнялись.
- Последовательный worker устраняет воспроизведённую гонку ответов, но иной timeout,
  отмена или сбой control plane всё ещё может закрыть транспорт. Для этого остаются
  диагностика и опциональный ReConnect, а не обещание отсутствия всех обрывов.
- Живой зависший MCP и внешний туннель без доступного terminal event по-прежнему
  нельзя надёжно признать исправными только по cached health/PID. Активный MCP ping
  не добавлен: второй писатель/читатель stdio небезопасен; новый probe через иной
  канал требует отдельного контракта. Логи теперь прямо обозначают границу проверки.
- Другие stdio MCP используют тот же upstream tunnel-client и потенциально подвержены
  этому классу гонки. Их рабочую параллельность в Godot-задаче не меняли вслепую;
  shared status/process regression tests прошли.
- Новый Connection Holder, переименование состояний, переписывание ReConnect,
  принудительный runtime health prerequisite, автозапуск ReConnect и auto-disable
  при Stop отклонены как лишние либо противоречащие требованиям.
- Замена Python/MCP/AnyIO, подавление `OSError` или повтор записи частично
  отправленного JSON в закрытый pipe отклонены: не устраняют доказанную причину.
- `max-inflight=1` отклонён после отрицательного теста. Используется именно
  `mcp.max-concurrent-requests=1`.
- Патч внешнего tunnel-client binary не выполнялся: достаточно проверенного
  параметра запуска в исходниках/упаковке ConHub. Upstream-исправление должно
  маршрутизировать shared ответы по ID либо сериализовать полный stdio exchange.
- Зависимости Python остаются с прежними диапазонами версий. Проверенная среда
  записана в `test-environment.txt`; вся будущая dependency matrix не проверялась.

## 8. Будущий Connection Holder

Сначала согласовать его режим по умолчанию и взаимодействие с opt-in ReConnect.
Нужен один координатор владения и восстановления, а не конкурирующие таймеры.
Разделять tunnel identity, реальный MCP roundtrip, editor project identity и
необязательный runtime; использовать bounded безопасный запрос через поддержанный
transport и учитывать занятость редактора. Сохранять подтверждение отказа,
single-flight, ограниченные попытки, отмену, защиту внешних процессов, process
generation и разреженные диагностические transitions. Не трактовать Stop как
deactivate при включённом ReConnect. Проверить fresh install, upstream cancellation,
paused/stopped runtime и длительную работу на реальном проекте перед стабильным выпуском.

## 9. Кандидат

Portable ZIP и SHA-256: `artifacts/godot-review/v0.5.6-rc.1/`.
Распакованная проверенная сборка: `artifacts/godot-review/ConHub-v0.5.6-rc.1/`.
Продукт отображает prerelease-версию `0.5.6-rc.1`. Имена exe/config/profile/ZIP
совместимы с v0.5.5. Ни архив, ни сборка не установлены поверх рабочего приложения.
Для stable v0.5.6 остаётся ручная приёмка live Godot/endurance и отдельное решение
о публикации; задача публикацию не выполняет.
