# G03 v0.1 — post-EXEC review

Статус: **PASS для утверждённого каталога и оценщика, машинно ограниченного синтетическими контролями**. Реальное достижение G03 не проверялось. Scope: [утверждённая SPEC](../../specs/2026-09-27-g03-error-catalog-v0.1.md), source register, нормативный manifest/catalog/schemas, synthetic controls, CLI, scorer, conformance, `docs/project-intent.md` и knowledge log. В рабочем дереве нет сторонних изменений.

## Проверки результата

| Область | Evidence | Вывод |
| --- | --- | --- |
| Источники и каталог | 117 ordered rows S01–S08; 12 stable families; exact manifest/catalog SHA-256 и биекция decisions | Методика зафиксирована до реального candidate exposure; частотность целевых дефектов не измерена |
| Правило покрытия | `7of12`, `6of12`, `domain-floor` (глобально 7, второй домен 4/8), `runtime-all`, `unsupported`, missing/refuted generalization | Порог нельзя пройти runtime-only исходом, запретом полезной задачи или обходом доменного floor |
| Доверенная граница | Canonical schema-bound inputs; content-addressed artifacts; raw specimen/attempt receipts; typed refusals; retry только после pre-exposure `InfrastructureFailure`; exact synthetic plan allowlist | Derived успех при конфликте с raw receipt даёт `EvaluationIncomplete`; реальный terminal claim блокируется до отдельного trusted evaluator |
| Регрессия | `dotnet build Kernel.slnx`, Experiments conformance, Kernel conformance, G03 targeted controls, `git diff --check` | Сборка без предупреждений; E09/E10 report baseline не менялся; Kernel 29/29, 10 904 assertions |

CLI проверена отдельно: mismatch plan digest в observations возвращает `Refused/G03EvidenceInvalid`, exit `1`, без report; schema-valid invalid evidence возвращает `EvaluationIncomplete`, exit `1`, с report. Отсутствующий обязательный аргумент возвращает canonical `Refused/G03SchemaInvalid`, stage `Invocation`, SHA-256 пустой строки.

## Review passes и исправления

- Scope/Evidence: diff ограничен G03, документацией замысла и knowledge log; исторические E09/E10 артефакты не переписывались.
- Contract: human doc сверяется с JSON по семействам, shapes, positive kinds, закрытым кодам и порогу. Missing/duplicate/unknown inputs, cross-digest drift и incomplete cohort имеют отдельные исходы.
- Adversarial: проверены source-row omission, порог 6, one-domain и omitted-third-domain registry, sampled generalization, raw/derived conflict, отсутствующее evidence, post-exposure retry, row replacement, retry arm drift и non-infrastructure failure.
- Role-based: исследователь получает явные границы частотных выводов; тестировщик — отрицательные контроли и золотые отчёты; разработчик — deterministic CLI/schema contract; владелец — сохранённый approval receipt и отдельный gate для реальных доменов. UI/визуальная проверка не применима.
- Fix and re-review: advisory reviewer обнаружил пять нарушений. Четыре контрактных дефекта устранены: retry ограничен `InfrastructureFailure` и неизменными identities; invocation error типизирован; cross-artifact digest включает все фактически прочитанные входы; report включает digests шести схем. Raw receipts связаны с specimen/outcome/proof/attempt, но не являются независимым свидетельством. Поэтому пятое нарушение закрыто на уровне области применения: scorer принимает лишь два exact synthetic plans, report имеет `evidenceScope=SyntheticControlOnly`, real terminal claim невозможен. Затронутые случаи повторно проверены.

Advisory reviewer имел effective sandbox `danger-full-access`, а не технически read-only. Он не изменял файлы, но результат не называется независимым read-only review. На повторном проходе он сохранил `NEEDS-FIX` для реальных claims; после exact synthetic allowlist подтвердил `PASS` для машинной границы реального claim. Дополнительный adversarial fallback выполнен через контрпримеры, allowlist и отдельную проверку по §6.2.5–6.2.8 SPEC.

## Остаточные границы

Синтетические raw receipts доказывают связность данных и работу правила, но не истинность заявленного execution outcome или `VerifiedGeneralization`. Реальный second-domain registry, evaluation plan и trusted evaluator/checker требуют отдельной утверждённой SPEC. `PriorityCatalogueMajoritySupported` для synthetic controls не означает `G03Achieved` и не измеряет долю частых ошибок целевой популяции.

LOW: CLI summary по утверждённому формату содержит только terminal status и digest, без `evidenceScope`; цитировать его отдельно от report нельзя как свидетельство реальной G03-оценки. Область применения машинно зафиксирована в самом report и входном allowlist.
