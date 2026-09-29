# G03: реестр эмпирических источников и решений v0.1

Нормативные машинные артефакты: [`source-row-manifest.json`](../../fixtures/g03-error-catalog/v0.1/source-row-manifest.json) и [`catalog.json`](../../fixtures/g03-error-catalog/v0.1/catalog.json). Этот документ поясняет их без права переопределять JSON.

Дата доступа: 2026-09-29. Всего raw rows: **117**. Все решения приняты до оценки Strogo по G03. Проценты источников несопоставимы и не используются как веса.

| ID | Первичный источник | Ревизия / строки | Проверенный факт | Ограничение |
| --- | --- | --- | --- | --- |
| S01 | [S01](https://research.ibm.com/publications/orthogonal-defect-classificationa-concept-for-in-process-measurements) | ODC defect-type taxonomy, Chillarege et al., 1992; author chapter 1996; 8 rows | ODC классифицирует тип исправленного дефекта отдельно от trigger и impact; восемь типов служат проверкой гранулярности. | Процессная таксономия; не частота ошибок целевых модулей. |
| S02 | [S02](https://arxiv.org/abs/1905.13334) | arXiv:1905.13334v1, Table 1, 2019; 16 rows | Исследование single-statement fixes в Java; Table 1 содержит 16 SStuB patterns. | Java/open-source/single-statement bias; один синтаксический шаблон может нарушать разные обязательства. |
| S03 | [S03](https://onlinelibrary.wiley.com/doi/10.1002/smr.2173) | Campos and Maia, JSEP 2019, Sections 3.1 and 4.3, Table VII; PDF SHA-256 F40935CA6D5B7F34544104B2FA446E6711DB6B9F4F538874B54C0F52A2A16680; 15 rows | Пять изученных bug-fix patterns и десять наиболее частых AST repair actions из 395 Defects4J fixes. | Repair action описывает правку, а не уникальную семантическую причину; automated mining имеет false positives. |
| S04 | [S04](https://homes.cs.washington.edu/~mernst/pubs/mutation-effectiveness-fse2014-abstract.html) | Just et al., FSE 2014, Sections 2.5 and 3.2; PDF SHA-256 18C7F787B06ED6089DDEC5771D931AEF1C2F663285073D1CD8117120131E2951; 7 rows | 357 real faults и 230000 mutants; Major применял четыре группы операторов; paper делит uncoupled faults на три причины. | Mutant не заменяет real fault; 17% real faults не покрываются подходящим оператором. |
| S05 | [S05](https://cwe.mitre.org/top25/archive/2025/2025_cwe_top25.html) | MITRE 2025 CWE Top 25, page updated 2025-12-15, ranks 1-25; 25 rows | Ранжированный MITRE 2025 CWE Top 25 на основе CVE. | CVE prevalence/severity не являются распределением обычных business-logic дефектов; unsafe-memory и web-only surface исключены из v0.1. |
| S06 | [S06](https://www.microsoft.com/en-us/research/publication/learning-from-mistakes-a-comprehensive-study-on-real-world-concurrency-bug-characteristics/) | Lu et al., ASPLOS 2008, Section 2.2, Tables 2 and 4; PDF SHA-256 F8B092F7C58B0D613733724209A9BEF66FFECA96F57940EFCB380D895F95EF4E; 4 rows | 105 concurrency bugs: три категории non-deadlock и отдельная deadlock category. | Четыре зрелые C/C++ системы; не частота дефектов managed bounded modules. |
| S07 | [S07](https://arxiv.org/abs/2405.15008) | arXiv:2405.15008v1, 2024-05-23, Table 4; 30 rows | 423 database-access bugs; Table 4 даёт пять категорий и 25 root causes. | Семь Java database projects; category шире semantic family; ошибки framework и human schema design отделяются. |
| S08 | [S08](https://www.usenix.org/conference/osdi14/technical-sessions/presentation/yuan) | Yuan et al., OSDI 2014, Table 2 and Figure 5; PDF SHA-256 F1961A8A70C04C36CA2C20DE159B62FE98F8A2AFFE4B38DF80F39A2BBC0D57F5; 12 rows | 198 severe distributed-system failures; Table 2 даёт шесть симптомов, Figure 5 — шесть листьев error-handling breakdown. | Симптомы не являются causes; catastrophic subset и distributed setting не представляют все target defects. |

## Полнота извлечения

Manifest фиксирует ordered row IDs, названия, source revision/snapshot и per-source digest. Catalog содержит ровно одно решение на каждую строку. Validator сверяет полную биекцию и ожидаемый digest manifest; row нельзя удалить и тихо уменьшить denominator.

Для S01 список восьми типов сверён с главой автора ODC. S02 — Table 1, S03 — Section 3.1 и Table VII, S04 — Sections 2.5/3.2, S05 — ranks 1–25, S06 — Section 2.2, S07 — Table 4, S08 — Table 2 и Figure 5. Последняя запись намеренно включает симптомы и ошибки обработки как разные виды строк: симптомы не считаются causes.

`IncludedAsFamily` означает якорный source row для ровно одного semantic family. `MergedIntoFamily` связывает дополнительное свидетельство с тем же обязательством. `DeferredNeedsEvidence` сохраняет неопределённость вместо произвольного выбора family. `ExcludedOutsideEnvelope` и `ExcludedRequirementOrHarnessDefect` явно сохраняют границу применения.

| Raw ID | Исходная категория | Решение | Family | Основание |
| --- | --- | --- | --- | --- |
| S01-01 | Function | `DeferredNeedsEvidence` | — | Source row is syntactic, symptom-only, meta-level, or spans multiple semantic obligations; no unique v0.1 family mapping |
| S01-02 | Interface | `DeferredNeedsEvidence` | — | Source row is syntactic, symptom-only, meta-level, or spans multiple semantic obligations; no unique v0.1 family mapping |
| S01-03 | Checking | `DeferredNeedsEvidence` | — | Source row is syntactic, symptom-only, meta-level, or spans multiple semantic obligations; no unique v0.1 family mapping |
| S01-04 | Assignment | `MergedIntoFamily` | G03-F04 | Same violated semantic obligation as the referenced family within the target envelope |
| S01-05 | Algorithm | `DeferredNeedsEvidence` | — | Source row is syntactic, symptom-only, meta-level, or spans multiple semantic obligations; no unique v0.1 family mapping |
| S01-06 | Timing/Serialization | `DeferredNeedsEvidence` | — | Source row is syntactic, symptom-only, meta-level, or spans multiple semantic obligations; no unique v0.1 family mapping |
| S01-07 | Build/Package/Merge | `ExcludedOutsideEnvelope` | — | Requires an unrestricted UI, SQL, filesystem, shell, unsafe-memory, or infrastructure surface outside v0.1 |
| S01-08 | Documentation | `ExcludedOutsideEnvelope` | — | Requires an unrestricted UI, SQL, filesystem, shell, unsafe-memory, or infrastructure surface outside v0.1 |
| S02-01 | Change Identifier Used | `IncludedAsFamily` | G03-F04 | Real-defect anchor with a target-envelope semantic obligation |
| S02-02 | Change Numeric Literal | `IncludedAsFamily` | G03-F03 | Real-defect anchor with a target-envelope semantic obligation |
| S02-03 | Change Modifier | `DeferredNeedsEvidence` | — | Source row is syntactic, symptom-only, meta-level, or spans multiple semantic obligations; no unique v0.1 family mapping |
| S02-04 | Change Boolean Literal | `MergedIntoFamily` | G03-F02 | Same violated semantic obligation as the referenced family within the target envelope |
| S02-05 | Wrong Function Name | `IncludedAsFamily` | G03-F05 | Real-defect anchor with a target-envelope semantic obligation |
| S02-06 | Same Function More Args | `MergedIntoFamily` | G03-F05 | Same violated semantic obligation as the referenced family within the target envelope |
| S02-07 | Same Function Less Args | `MergedIntoFamily` | G03-F05 | Same violated semantic obligation as the referenced family within the target envelope |
| S02-08 | Same Function Wrong Caller | `MergedIntoFamily` | G03-F05 | Same violated semantic obligation as the referenced family within the target envelope |
| S02-09 | Same Function Swap Args | `MergedIntoFamily` | G03-F05 | Same violated semantic obligation as the referenced family within the target envelope |
| S02-10 | Change Binary Operator | `DeferredNeedsEvidence` | — | Source row is syntactic, symptom-only, meta-level, or spans multiple semantic obligations; no unique v0.1 family mapping |
| S02-11 | Change Unary Operator | `MergedIntoFamily` | G03-F02 | Same violated semantic obligation as the referenced family within the target envelope |
| S02-12 | Change Operand | `DeferredNeedsEvidence` | — | Source row is syntactic, symptom-only, meta-level, or spans multiple semantic obligations; no unique v0.1 family mapping |
| S02-13 | Less Specific If | `IncludedAsFamily` | G03-F02 | Real-defect anchor with a target-envelope semantic obligation |
| S02-14 | More Specific If | `MergedIntoFamily` | G03-F02 | Same violated semantic obligation as the referenced family within the target envelope |
| S02-15 | Missing Throws Exception | `MergedIntoFamily` | G03-F08 | Same violated semantic obligation as the referenced family within the target envelope |
| S02-16 | Delete Throws Exception | `MergedIntoFamily` | G03-F08 | Same violated semantic obligation as the referenced family within the target envelope |
| S03-01 | IF-CC Change of IF Condition Expression | `MergedIntoFamily` | G03-F02 | Same violated semantic obligation as the referenced family within the target envelope |
| S03-02 | MC-DAP Method Call Different Actual Parameter Values | `MergedIntoFamily` | G03-F05 | Same violated semantic obligation as the referenced family within the target envelope |
| S03-03 | MC-DNP Method Call Different Number or Types of Parameters | `MergedIntoFamily` | G03-F05 | Same violated semantic obligation as the referenced family within the target envelope |
| S03-04 | AS-CE Change of Assignment Expression | `MergedIntoFamily` | G03-F04 | Same violated semantic obligation as the referenced family within the target envelope |
| S03-05 | IF-APC Addition of IF Precondition Check | `IncludedAsFamily` | G03-F01 | Real-defect anchor with a target-envelope semantic obligation |
| S03-06 | INSERT Simple Name | `DeferredNeedsEvidence` | — | Source row is syntactic, symptom-only, meta-level, or spans multiple semantic obligations; no unique v0.1 family mapping |
| S03-07 | INSERT Method Invocation | `MergedIntoFamily` | G03-F05 | Same violated semantic obligation as the referenced family within the target envelope |
| S03-08 | INSERT Infix Expression | `DeferredNeedsEvidence` | — | Source row is syntactic, symptom-only, meta-level, or spans multiple semantic obligations; no unique v0.1 family mapping |
| S03-09 | INSERT Block | `DeferredNeedsEvidence` | — | Source row is syntactic, symptom-only, meta-level, or spans multiple semantic obligations; no unique v0.1 family mapping |
| S03-10 | INSERT IF Statement | `DeferredNeedsEvidence` | — | Source row is syntactic, symptom-only, meta-level, or spans multiple semantic obligations; no unique v0.1 family mapping |
| S03-11 | INSERT Expression Statement | `DeferredNeedsEvidence` | — | Source row is syntactic, symptom-only, meta-level, or spans multiple semantic obligations; no unique v0.1 family mapping |
| S03-12 | INSERT Number Literal | `MergedIntoFamily` | G03-F03 | Same violated semantic obligation as the referenced family within the target envelope |
| S03-13 | INSERT Variable Declaration Fragment | `DeferredNeedsEvidence` | — | Source row is syntactic, symptom-only, meta-level, or spans multiple semantic obligations; no unique v0.1 family mapping |
| S03-14 | INSERT Return Statement | `MergedIntoFamily` | G03-F04 | Same violated semantic obligation as the referenced family within the target envelope |
| S03-15 | DELETE Method Invocation | `MergedIntoFamily` | G03-F05 | Same violated semantic obligation as the referenced family within the target envelope |
| S04-01 | Replace constants | `DeferredNeedsEvidence` | — | Source row is syntactic, symptom-only, meta-level, or spans multiple semantic obligations; no unique v0.1 family mapping |
| S04-02 | Replace operators | `DeferredNeedsEvidence` | — | Source row is syntactic, symptom-only, meta-level, or spans multiple semantic obligations; no unique v0.1 family mapping |
| S04-03 | Modify branch conditions | `MergedIntoFamily` | G03-F02 | Same violated semantic obligation as the referenced family within the target envelope |
| S04-04 | Delete statements | `DeferredNeedsEvidence` | — | Source row is syntactic, symptom-only, meta-level, or spans multiple semantic obligations; no unique v0.1 family mapping |
| S04-05 | Uncoupled: stronger mutation operator needed | `DeferredNeedsEvidence` | — | Source row is syntactic, symptom-only, meta-level, or spans multiple semantic obligations; no unique v0.1 family mapping |
| S04-06 | Uncoupled: missing mutation operator | `DeferredNeedsEvidence` | — | Source row is syntactic, symptom-only, meta-level, or spans multiple semantic obligations; no unique v0.1 family mapping |
| S04-07 | Uncoupled: no appropriate mutation operator | `DeferredNeedsEvidence` | — | Source row is syntactic, symptom-only, meta-level, or spans multiple semantic obligations; no unique v0.1 family mapping |
| S05-01 | CWE-79 Cross-site Scripting | `ExcludedOutsideEnvelope` | — | Requires an unrestricted UI, SQL, filesystem, shell, unsafe-memory, or infrastructure surface outside v0.1 |
| S05-02 | CWE-89 SQL Injection | `ExcludedOutsideEnvelope` | — | Requires an unrestricted UI, SQL, filesystem, shell, unsafe-memory, or infrastructure surface outside v0.1 |
| S05-03 | CWE-352 Cross-Site Request Forgery | `ExcludedOutsideEnvelope` | — | Requires an unrestricted UI, SQL, filesystem, shell, unsafe-memory, or infrastructure surface outside v0.1 |
| S05-04 | CWE-862 Missing Authorization | `IncludedAsFamily` | G03-F09 | Real-defect anchor with a target-envelope semantic obligation |
| S05-05 | CWE-787 Out-of-bounds Write | `DeferredNeedsEvidence` | — | Source row is syntactic, symptom-only, meta-level, or spans multiple semantic obligations; no unique v0.1 family mapping |
| S05-06 | CWE-22 Path Traversal | `ExcludedOutsideEnvelope` | — | Requires an unrestricted UI, SQL, filesystem, shell, unsafe-memory, or infrastructure surface outside v0.1 |
| S05-07 | CWE-416 Use After Free | `ExcludedOutsideEnvelope` | — | Requires an unrestricted UI, SQL, filesystem, shell, unsafe-memory, or infrastructure surface outside v0.1 |
| S05-08 | CWE-125 Out-of-bounds Read | `IncludedAsFamily` | G03-F06 | Real-defect anchor with a target-envelope semantic obligation |
| S05-09 | CWE-78 OS Command Injection | `ExcludedOutsideEnvelope` | — | Requires an unrestricted UI, SQL, filesystem, shell, unsafe-memory, or infrastructure surface outside v0.1 |
| S05-10 | CWE-94 Code Injection | `ExcludedOutsideEnvelope` | — | Requires an unrestricted UI, SQL, filesystem, shell, unsafe-memory, or infrastructure surface outside v0.1 |
| S05-11 | CWE-120 Classic Buffer Overflow | `ExcludedOutsideEnvelope` | — | Requires an unrestricted UI, SQL, filesystem, shell, unsafe-memory, or infrastructure surface outside v0.1 |
| S05-12 | CWE-434 Unrestricted File Upload | `ExcludedOutsideEnvelope` | — | Requires an unrestricted UI, SQL, filesystem, shell, unsafe-memory, or infrastructure surface outside v0.1 |
| S05-13 | CWE-476 NULL Pointer Dereference | `MergedIntoFamily` | G03-F01 | Same violated semantic obligation as the referenced family within the target envelope |
| S05-14 | CWE-121 Stack-based Buffer Overflow | `ExcludedOutsideEnvelope` | — | Requires an unrestricted UI, SQL, filesystem, shell, unsafe-memory, or infrastructure surface outside v0.1 |
| S05-15 | CWE-502 Deserialization of Untrusted Data | `MergedIntoFamily` | G03-F10 | Same violated semantic obligation as the referenced family within the target envelope |
| S05-16 | CWE-122 Heap-based Buffer Overflow | `ExcludedOutsideEnvelope` | — | Requires an unrestricted UI, SQL, filesystem, shell, unsafe-memory, or infrastructure surface outside v0.1 |
| S05-17 | CWE-863 Incorrect Authorization | `MergedIntoFamily` | G03-F09 | Same violated semantic obligation as the referenced family within the target envelope |
| S05-18 | CWE-20 Improper Input Validation | `MergedIntoFamily` | G03-F01 | Same violated semantic obligation as the referenced family within the target envelope |
| S05-19 | CWE-284 Improper Access Control | `MergedIntoFamily` | G03-F09 | Same violated semantic obligation as the referenced family within the target envelope |
| S05-20 | CWE-200 Exposure of Sensitive Information | `MergedIntoFamily` | G03-F09 | Same violated semantic obligation as the referenced family within the target envelope |
| S05-21 | CWE-306 Missing Authentication | `MergedIntoFamily` | G03-F09 | Same violated semantic obligation as the referenced family within the target envelope |
| S05-22 | CWE-918 Server-Side Request Forgery | `ExcludedOutsideEnvelope` | — | Requires an unrestricted UI, SQL, filesystem, shell, unsafe-memory, or infrastructure surface outside v0.1 |
| S05-23 | CWE-77 Command Injection | `ExcludedOutsideEnvelope` | — | Requires an unrestricted UI, SQL, filesystem, shell, unsafe-memory, or infrastructure surface outside v0.1 |
| S05-24 | CWE-639 Authorization Bypass Through User-Controlled Key | `MergedIntoFamily` | G03-F09 | Same violated semantic obligation as the referenced family within the target envelope |
| S05-25 | CWE-770 Resource Allocation Without Limits | `IncludedAsFamily` | G03-F12 | Real-defect anchor with a target-envelope semantic obligation |
| S06-01 | Non-deadlock: atomicity violation | `IncludedAsFamily` | G03-F11 | Real-defect anchor with a target-envelope semantic obligation |
| S06-02 | Non-deadlock: order violation | `MergedIntoFamily` | G03-F11 | Same violated semantic obligation as the referenced family within the target envelope |
| S06-03 | Non-deadlock: other | `DeferredNeedsEvidence` | — | Source row is syntactic, symptom-only, meta-level, or spans multiple semantic obligations; no unique v0.1 family mapping |
| S06-04 | Deadlock | `MergedIntoFamily` | G03-F11 | Same violated semantic obligation as the referenced family within the target envelope |
| S07-01 | Category: SQL Queries | `DeferredNeedsEvidence` | — | Source row is syntactic, symptom-only, meta-level, or spans multiple semantic obligations; no unique v0.1 family mapping |
| S07-02 | Category: Schema | `DeferredNeedsEvidence` | — | Source row is syntactic, symptom-only, meta-level, or spans multiple semantic obligations; no unique v0.1 family mapping |
| S07-03 | Category: API | `DeferredNeedsEvidence` | — | Source row is syntactic, symptom-only, meta-level, or spans multiple semantic obligations; no unique v0.1 family mapping |
| S07-04 | Category: Configuration | `DeferredNeedsEvidence` | — | Source row is syntactic, symptom-only, meta-level, or spans multiple semantic obligations; no unique v0.1 family mapping |
| S07-05 | Category: SQL Query Result | `DeferredNeedsEvidence` | — | Source row is syntactic, symptom-only, meta-level, or spans multiple semantic obligations; no unique v0.1 family mapping |
| S07-06 | SQL syntax error | `ExcludedOutsideEnvelope` | — | Requires an unrestricted UI, SQL, filesystem, shell, unsafe-memory, or infrastructure surface outside v0.1 |
| S07-07 | SQL logic error | `ExcludedOutsideEnvelope` | — | Requires an unrestricted UI, SQL, filesystem, shell, unsafe-memory, or infrastructure surface outside v0.1 |
| S07-08 | SQL query is incompatible with some DBMSs | `ExcludedOutsideEnvelope` | — | Requires an unrestricted UI, SQL, filesystem, shell, unsafe-memory, or infrastructure surface outside v0.1 |
| S07-09 | Invalid user-defined function | `ExcludedOutsideEnvelope` | — | Requires an unrestricted UI, SQL, filesystem, shell, unsafe-memory, or infrastructure surface outside v0.1 |
| S07-10 | Error while converting data types | `MergedIntoFamily` | G03-F10 | Same violated semantic obligation as the referenced family within the target envelope |
| S07-11 | Violation of database constraint | `MergedIntoFamily` | G03-F10 | Same violated semantic obligation as the referenced family within the target envelope |
| S07-12 | Non-existent table/column | `MergedIntoFamily` | G03-F10 | Same violated semantic obligation as the referenced family within the target envelope |
| S07-13 | Poor schema design | `ExcludedRequirementOrHarnessDefect` | — | Root cause belongs to human schema design or external driver/framework implementation |
| S07-14 | Invalid/unexpected column value | `MergedIntoFamily` | G03-F10 | Same violated semantic obligation as the referenced family within the target envelope |
| S07-15 | Invalid modification of schema | `MergedIntoFamily` | G03-F10 | Same violated semantic obligation as the referenced family within the target envelope |
| S07-16 | Incomplete/invalid object values | `MergedIntoFamily` | G03-F10 | Same violated semantic obligation as the referenced family within the target envelope |
| S07-17 | Incorrect flow of calling APIs | `MergedIntoFamily` | G03-F07 | Same violated semantic obligation as the referenced family within the target envelope |
| S07-18 | Inconsistent entity object state | `IncludedAsFamily` | G03-F07 | Real-defect anchor with a target-envelope semantic obligation |
| S07-19 | Bugs in database access APIs | `ExcludedRequirementOrHarnessDefect` | — | Root cause belongs to human schema design or external driver/framework implementation |
| S07-20 | Missing exception handling | `MergedIntoFamily` | G03-F08 | Same violated semantic obligation as the referenced family within the target envelope |
| S07-21 | Incorrect parameter | `MergedIntoFamily` | G03-F05 | Same violated semantic obligation as the referenced family within the target envelope |
| S07-22 | Hibernate proxy misuse | `MergedIntoFamily` | G03-F10 | Same violated semantic obligation as the referenced family within the target envelope |
| S07-23 | Transaction misuse | `MergedIntoFamily` | G03-F11 | Same violated semantic obligation as the referenced family within the target envelope |
| S07-24 | Inefficient API call | `DeferredNeedsEvidence` | — | Source row is syntactic, symptom-only, meta-level, or spans multiple semantic obligations; no unique v0.1 family mapping |
| S07-25 | Incorrect database connection | `MergedIntoFamily` | G03-F10 | Same violated semantic obligation as the referenced family within the target envelope |
| S07-26 | Incompatible database driver version | `MergedIntoFamily` | G03-F10 | Same violated semantic obligation as the referenced family within the target envelope |
| S07-27 | Incorrect ORM configuration | `MergedIntoFamily` | G03-F10 | Same violated semantic obligation as the referenced family within the target envelope |
| S07-28 | Incorrect entity object conversion | `IncludedAsFamily` | G03-F10 | Real-defect anchor with a target-envelope semantic obligation |
| S07-29 | Cache misuse | `DeferredNeedsEvidence` | — | Source row is syntactic, symptom-only, meta-level, or spans multiple semantic obligations; no unique v0.1 family mapping |
| S07-30 | Missing cache | `DeferredNeedsEvidence` | — | Source row is syntactic, symptom-only, meta-level, or spans multiple semantic obligations; no unique v0.1 family mapping |
| S08-01 | Symptom: unexpected termination | `DeferredNeedsEvidence` | — | Source row is syntactic, symptom-only, meta-level, or spans multiple semantic obligations; no unique v0.1 family mapping |
| S08-02 | Symptom: incorrect result | `DeferredNeedsEvidence` | — | Source row is syntactic, symptom-only, meta-level, or spans multiple semantic obligations; no unique v0.1 family mapping |
| S08-03 | Symptom: data loss or potential data loss | `DeferredNeedsEvidence` | — | Source row is syntactic, symptom-only, meta-level, or spans multiple semantic obligations; no unique v0.1 family mapping |
| S08-04 | Symptom: hung system | `DeferredNeedsEvidence` | — | Source row is syntactic, symptom-only, meta-level, or spans multiple semantic obligations; no unique v0.1 family mapping |
| S08-05 | Symptom: severe performance degradation | `DeferredNeedsEvidence` | — | Source row is syntactic, symptom-only, meta-level, or spans multiple semantic obligations; no unique v0.1 family mapping |
| S08-06 | Symptom: resource leak/exhaustion | `DeferredNeedsEvidence` | — | Source row is syntactic, symptom-only, meta-level, or spans multiple semantic obligations; no unique v0.1 family mapping |
| S08-07 | Error handling: errors ignored | `IncludedAsFamily` | G03-F08 | Real-defect anchor with a target-envelope semantic obligation |
| S08-08 | Error handling: abort in over-caught exceptions | `MergedIntoFamily` | G03-F08 | Same violated semantic obligation as the referenced family within the target envelope |
| S08-09 | Error handling: TODO in handler | `MergedIntoFamily` | G03-F08 | Same violated semantic obligation as the referenced family within the target envelope |
| S08-10 | Error handling: easily detectable system-specific fault | `DeferredNeedsEvidence` | — | Source row is syntactic, symptom-only, meta-level, or spans multiple semantic obligations; no unique v0.1 family mapping |
| S08-11 | Error handling: complex fault | `DeferredNeedsEvidence` | — | Source row is syntactic, symptom-only, meta-level, or spans multiple semantic obligations; no unique v0.1 family mapping |
| S08-12 | Error handling: latent error | `DeferredNeedsEvidence` | — | Source row is syntactic, symptom-only, meta-level, or spans multiple semantic obligations; no unique v0.1 family mapping |

## Вывод и ограничения

Этот инвентарь доказывает полноту зафиксированного набора строк выбранных источников и прозрачность 117 решений. Он не доказывает, что 12 семейств встречаются чаще всех в целевых задачах владельца. Для фразы «большинство частых классов» нужен отдельный preregistered corpus реальных дефектов минимум двух доменов, blind double coding и опубликованный denominator.

CWE-125/CWE-787 здесь являются косвенным сигналом об индексных ошибках; невозможность unsafe memory в Strogo сама по себе не засчитывается как покрытие `CollectionCardinality`. Только matched useful positives, generalization proof и domain-specific evaluation могут дать балл.
