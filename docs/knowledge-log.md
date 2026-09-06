# Журнал знаний Strogo

Основной журнал для устойчивых инсайтов по этапам проекта: подтверждения гипотез, опровержения, контрпримеры, ограничения, процедурные решения и негативные результаты.

## K-E05-001

- Дата / фаза: 2026-09-06 / SPEC.
- Тип / статус: Decision / Accepted.
- Утверждение: для E05 после каждой значимой правки вносятся заметки в `docs/knowledge-log.md`, включая подтверждения/опровержения гипотез и отрицательные результаты.
- Scope: AGENTS rule, E05-spec.
- Evidence: `AGENTS.md`, `specs/2026-09-06-composable-verified-modules-v0.2.md` (обновлённый раздел 5).
- Последствие: проекту больше не требуется дублировать журнал на `docs/e05-knowledge-log.md`; все новые знания E05 идут в этот файл.
- Supersedes / supersededBy: заменяет разрозненные ссылки на `docs/e05-knowledge-log.md` в текущем черновике SPEC.

## K-E05-002

- Дата / фаза: 2026-09-06 / SPEC.
- Тип / статус: Constraint / Qualified.
- Утверждение: проверка `approval` только по hash недостаточна для модели доверия в E05; необходима provenance-цепочка (proof+admission+build) и проверяемые поля подписи.
- Scope: E05; AC2/AC3/AC6, trust model.
- Evidence: обновлённая секция 6.4 в `specs/2026-09-06-composable-verified-modules-v0.2.md`.
- Последствие: без подписи и детерминированных digest-цепей любой tamper-путём может имитировать ранее выданный admission.
- Supersedes / supersededBy: уточняет E05-K09.

## K-E05-003

- Дата / фаза: 2026-09-06 / SPEC.
- Тип / статус: Qualification / Confirmed.
- Утверждение: fold-инварианты и assertions должны иметь явную точку приложения (locus) для replay-аудита; без locus нельзя считать coverage наблюдаемым и полным.
- Scope: G04, proof obligations, AC2/AC3.
- Evidence: обновлённая секция 6.2 в `specs/2026-09-06-composable-verified-modules-v0.2.md`.
- Последствие: доказательства без locus не считаются достаточными для exact outcome в E05.
- Supersedes / supersededBy: уточняет E05-K08.

## K-E05-004

- Дата / фаза: 2026-09-06 / SPEC.
- Тип / статус: Qualification / Confirmed.
- Утверждение: proof статусы `Timeout`/`ToolError` относятся к knowledge gap и не закрывают обязательства точного результата.
- Scope: execution gates, AC2/AC3/AC6.
- Evidence: `specs/2026-09-06-composable-verified-modules-v0.2.md`, раздел 6.2 и 6.4.
- Последствие: такие статусы требуют explicit repair/перезапуска и отдельной записи в журнале.
- Supersedes / supersededBy: уточняет E05-K04 (как методика обработки неполных proof результатов).
## K-E05-010

- Дата / фаза: 2026-09-06 / EXEC (checkpoint 1-4).
- Тип / статус: Decision-Checkpoint / Confirmed.
- Утверждение: в `strogo` добавлены рабочие артефакты checkpoint: структура `src/Strogo.Modules` с моделью AST/типов, лексером, парсером и компилятором IR, верифицированная фикстурно-конформансная проверка и фиксация в `docs/modules-v0.2.md`.
- Scope: E05; шаги 1–4 (структура кода, формальная нотация, лексер+парсер, compiler IR).
- Evidence: `src/Strogo.Modules/*.cs`, `tests/Strogo.Modules.Conformance/*`, `fixtures/modules-v0.2/*.json`, `docs/fixtures/modules-v0.2/*.json`, `docs/modules-v0.2.md`, сборка `dotnet build src/Strogo.Modules/Strogo.Modules.csproj`.
- Последствие: можно переходить к следующему checkpoint (formal semantics/validator, proof hooks) без повторной разработки формата.
- Supersedes / supersededBy: закрывает промежуточный пробел между сырой идеей и исполняемой библиотекой, без изменений в целевых гарантиях G01–G06.
