# Дополнение: воспроизведённая причина закрытия stdio

Это дополнение к первоначальному ревью составлено после обнаружения локальных
исходников tunnel-client, до применения изменения параметров запуска Godot.
Первоначальное ревью не переписывается задним числом.

## F6 / P1 — неверное распределение параллельных ответов main stdio

Исходники: C:/Users/kosti/chatgpt-blender-mcp/tunnel-client-v0.0.9--context-conduit-topaz-all.
`pkg/dispatcher/internal/processor.go`, `mcpProcessor.forwardResponses`: несколько
обработчиков читают общий IOTransport; response.ID сравнивается с ID конкретного
запроса. Ответ с корректным ID другого запроса считается `Received response
without valid ID`, после чего defer закрывает connection. Закрытие доходит до
общих stdin/stdout pipe. Сериализующая обёртка настроена для harpoon, но не main.

Проверен фактически установленный binary, без Godot и MCP SDK:
- SHA-256: 6cf9b0ba8f01a661bb040cbf5223c725beef51e3aa6fe6ebe08fb9d364f8334a.
- Версия из его локального /api/status: 0.0.9+62b9b42f698ec5319d2115e0c0ff1dcf6557d7ae.
- Подставной control plane только на loopback; приватные пустые config/profile/env,
  dummy key, отдельные динамические health-порты, ограниченное время, cleanup только
  созданного тестом дерева процессов. Production профили/ключи/Godot не используются.
- Подставной stdio Python получает восемь уникальных JSON-RPC запросов и отправляет
  целые корректные ответы с разными сроками завершения под блокировкой stdout.

| Запуск | Результат |
|---|---|
| Штатная параллельность | rpc-0 получает rpc-5; rpc-2 получает rpc-7. Invalid-ID → закрытые pipe → Python errno 22 → stdio exit/degraded, 502. Только 6 terminal responses из 8. |
| control-plane.max-inflight=1 | rpc-0 получает rpc-1. Тот же сбой, 7 terminal responses. Это размер очереди, не число активных workers. |
| mcp.max-concurrent-requests=1 | Все 8 ответов HTTP200 с правильными request/response ID; нет invalid-ID, closed pipe, errno22, degraded exit. |

Во всех трёх случаях /readyz HTTP200. После смерти fake MCP канал main всё ещё
enabled=true/probe_status=ok. Исходные артефакты: artifacts/godot-review/upstream-repro/
{default-v4,single-inflight-v4,single-worker-v4}/{result.json,normalized.json,tunnel.log,mcp-events.jsonl,isolation.json}.

## Выводы и предел доказательства

Это подтверждённый дефект установленного tunnel-client, воспроизводящий механизм
инцидента и порядок invalid-ID → closed pipe → stdout EINVAL. MCP SDK и Godot для
воспроизведения не нужны. Python 3.14 не является доказанной первопричиной:
ошибку записи вызывает закрывшийся upstream pipe, а не несовместимость SDK.
Для исторического запроса из 13:54:45 невозможно восстановить точную пару ID:
в production-логе отсутствует DEBUG-корреляция и содержимое ответов. Поэтому
не утверждаем, что исторический конкретный пакет доказан побайтно.

## Минимальное исправление и риск

ConHub должен добавлять `--mcp.max-concurrent-requests 1` только своему Godot tunnel
при каждом Start/Legacy/ALT. Worker удерживает весь processor.Process до окончания
forwardResponses. Ограничение размера очереди недостаточно. Чужой tunnel-client
binary и внешние профили не изменяются; исправление воспроизводимо из исходников
ConHub и попадает в упаковку.

Цена: последовательное выполнение Godot-запросов, включая ожидание ответа медленного
инструмента; собственные bridge/deadline ограничения остаются. Другие MCP имеют
тот же upstream binary и потенциально тот же класс риска, но их параметры запуска
в данной Godot-задаче не меняются. Внешний Godot launcher должен сам применять
этот параметр до upstream-исправления. Connection Holder не создаётся.

Нужны регрессии: параметр действительно добавляется Godot и отсутствует у прочих
адаптеров; installed-binary loopback test доставляет все ответы при одном worker;
сборка/упаковка и прежняя девятипопыточная ReConnect-схема сохраняются.
