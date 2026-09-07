# E06 — Профили переносимого исполнения и библиотек v0.1

## 0. Метаданные
- Тип (профиль): `product-system-design`, архитектура backend/runtime и исследовательский conformance.
- Владелец: человек владеет смыслом целей и утверждает эту смену порядка экспериментов; агент владеет реализацией утверждённого контракта.
- Масштаб: large — новый multi-runtime contract, package/evidence boundary и межкомпонентная проверка.
- Целевое семейство / behavior baseline: прямой эксперимент G02 и backend/ABI preservation; supporting evidence для механизмов G01/G04 без нового end-to-end claim, потому что fixture не имеет human contract approval/admission; подготовка измерений G06 без заявления о его достижении. Baseline — E05 Modules v0.2 и ещё не реализованные owner-composite v0.3/fold v0.1. Two-stage admission изучается как ограничение, но не меняется и не требуется.
- Поверхность: локальный Codex в репозитории Strogo.
- Effective runtime: для SPEC не влияет на смысл. Будущий EXEC использует закреплённые Dafny 4.11.0 и .NET SDK 10.0.400 для build. До первого platform run отдельно закрепляются exact .NET runtime 10.0.x archive/host/runtime digests для каждой ОС и exact JDK 17 vendor/build/archive digest для каждой ОС; номер SDK не подменяет runtime identity.
- Eval baseline / evidence: один заранее фиксированный validation-only pure module с `I64`, `Bool`, record, bounded `Seq`, `if`, local call и одним bounded `fold`; одинаковые canonical inputs и outcomes на C#/.NET и Java/JVM в Windows x64 и Linux x64. До EXEC такого evidence нет.
- Целевой релиз / ветка: локальная `main`; push, CI, release и публикация GitHub не входят в approval.
- Ограничения: SPEC не разрешает EXEC до реализации owner-composite и fold зависимостей. Платформенная матрица ограничена Windows x64 и Linux x64; browser, macOS, Arm64, mobile и embedded остаются отдельными строками будущих экспериментов. E06 packages являются только validation artifacts, несовместимы с E05 production schema и не добавляют production loader API.
- Связанные ссылки: [цели](../docs/project-intent.md), [E05 baseline](2026-09-06-composable-verified-modules-v0.2.md), [owner composite v0.3](2026-09-07-e05-owner-composite-contract-v0.3.md), [fold v0.1](2026-09-07-e05-fold-regions-and-invariants-v0.1.md), [two-stage admission](2026-09-07-e05-two-stage-admission-amendment.md), [предыдущий курс на Wasm](2026-09-04-controlled-language-experiment-v0.1.md#628-переносимость-и-библиотеки-архитектурный-курс).
- Instruction stack: central `creator-vibe-lens`, `model-behavior-baseline`, `quest-governance`, `quest-mode`, `tool-execution-baseline`, `spec-linter`, `spec-rubric`, `review-loops`, профиль `product-system-design`; локальный `AGENTS.md` Strogo.

## 1. Overview / Цель

Определить первый проверяемый путь от одного proof-verified validation-модуля Strogo к библиотечным артефактам для двух зрелых управляемых сред, не передавая платформенную поддержку собственному runtime Strogo и не имитируя отсутствующее human contract approval.

Вместо немедленного написания прямого Wasm backend сначала проверяется более дешёвая архитектурная гипотеза: один и тот же generated Dafny source после общего proof переводится штатными backend Dafny в C# и Java, а .NET и JVM отвечают за ОС, CPU, JIT и базовый runtime. Этот этап должен показать, можно ли отделить семантику и доказательство от target ABI, не скрыв backend-specific расхождения.

Outcome contract:
- Success means: один неизменный validation module/owner/proof identity порождает два target validation packages; byte-identical package каждого профиля фактически исполняется в изолированном validation workflow в Windows x64 и Linux x64; canonical outcomes и структурированные отказы совпадают с owner/reference oracle; package не содержит E05 `build-manifest.json`/`admission.json`, а E06 не экспортирует production load/admit operation.
- Итоговый артефакт / output: C#/.NET validation library profile, Java/JVM validation library profile, target adapters, два `portability-manifest.v0.1`, canonical portability report и человекочитаемая projection матрицы. Отдельная будущая SPEC использует полученные данные для portable package/admission schema; E06 v0.1 не создаёт contract approval, release admission или production-loadable package.
- Stop rules: любой drift source/owner/proof/toolchain/artifact, unsupported construct, backend diagnostic, различие outcome/error, отсутствие фактического runner или невоспроизводимый build оставляет соответствующую строку `Failed`/`Unavailable`; строка не получает `Portable`. При несовпадении backend semantics не ослаблять owner contract и не подменять входы.

## 2. Текущее состояние (AS-IS)

- E04 доводит один ограниченный модуль до ReadyToRun `win-x64`; это один фактически проверенный platform row.
- E05 строит canonical source → typed IR → Dafny → C# и уже исполняет scalar/composite reference values, но пока не имеет законченных composite owner contracts, fold, package/admission и публичного runtime facade.
- Утверждённый исследовательский курс называет Core Wasm следующим backend и WIT/Component Model следующим уровнем библиотек, но не определяет промежуточный portable package contract, platform matrix или связь одного proof с несколькими target artifacts.
- Закреплённый локальный Dafny 4.11.0 фактически предлагает `translate cs`, `java`, `js`, `go`, `py`, `cpp`, `rs`, `dfy`. Его help отдельно предупреждает об ограничениях C++; наличие команды само по себе не доказывает зрелость backend или эквивалентность semantics.
- На Windows доступны .NET 10.0.400 и JDK 17. WSL2 Ubuntu 24.04/x64 доступен, но .NET, Java и Wasmtime внутри него сейчас отсутствуют. Это provisioning gap, а не отрицательный результат языка.
- Pre-approval feasibility check на существующем `verification/task-graph-v1/Contract.dfy` подтвердил: Dafny 4.11.0 переводит его в 15 Java source files; `javac 17 --release 17 -Xlint:all,-cast -Werror` с pinned `DafnyRuntime.jar` создаёт 14 class files без diagnostics. Полный `-Xlint:all` даёт 13 предупреждений только категории `cast` в generated Dafny source, поэтому profile обязан отличать upstream generated-source policy от более строгой policy нашего adapter.
- Два clean `dafny build -t java --no-verify --enforce-determinism` создали JAR одинаковой длины `156757` и с 106 одинаковыми entry names/content digests, но разными whole-file SHA-256 (`00ed96d…e1d07` и `4e0aef8…495b6`) из-за entry timestamps. Repack обоих наборов pinned JDK `jar` с manifest-first explicit ordinal file list, `--no-manifest --no-compress --date=1980-01-01T00:00:02Z` дал byte-equal JAR SHA-256 `8ec403c…d15fb`. Оба имеют 105 regular entries, STORED method, один timestamp, UTF-8 flag и только exact JAR marker `FE CA 00 00` у первого manifest entry. Это packaging feasibility, а не E06 semantics evidence: запуск использовал прежний workload и `--no-verify`.

Официальные основания, проверенные 2026-09-07:

| Источник | Установленный факт | Граница вывода |
| --- | --- | --- |
| [Dafny README](https://github.com/dafny-lang/dafny/blob/master/README.md) | Dafny компилирует проверенные программы в C#, Go, Python, Java и JavaScript | Документ не обещает одинаковую ABI или доказанный compiler correctness |
| [Dafny compile target FAQ](https://dafny.org/latest/HowToFAQ/FAQCompileTargets) | C#, Java, JavaScript и Go названы well-supported в датированном ответе 3.7.3 | Статус требует проверки на закреплённой 4.11.0; Rust не выбран по одному наличию CLI command |
| [Dafny target-specific standard libraries](https://github.com/dafny-lang/dafny/blob/master/Source/DafnyStandardLibraries/CONTRIBUTING.md) | Extern implementations и часть standard library различаются по target | Imports/effects нельзя считать автоматически переносимыми |
| [.NET Native AOT](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/) | Native AOT выпускает self-contained native artifact per OS/architecture и запрещает ряд dynamic features | Он не даёт один бинарник для всех платформ; это отдельный profile после managed portability |
| [WASI releases](https://wasi.dev/releases) | Component Model даёт typed cross-language composition; WASI 0.3 current, а 0.2 пока поддерживается шире | Версию component toolchain нельзя выбрать без отдельного executable spike |

Публичная проверка направления: в [дискуссии Posting Board](https://getpostingboard.dev/b/t/e1ecc91e-d19b-45f0-8dd7-2b6ecbfe5c8e) опубликован reply `15979f1f-cdd1-4b92-8bc4-0378238cd281` с предложением E06A и просьбой назвать минимальный counterexample/критерий, при котором C#/Java lane не окупается. После review исходная формулировка про target admission признана неверной: E05 package contract .NET-specific. Публичная поправка `17a295a2-9b34-4e12-80a0-b38fc681d1a9` фиксирует validation-only boundary. Feasibility result и вопрос о риске скрытого drift при JAR normalization опубликованы/read back как reply `2873a93d-4ca2-4046-b7f6-81290c502997` (#9521). Ответ `0cb1efdf-babc-44df-beab-83fe60020411` (#9524) дал применимый counterexample: duplicate ZIP entries могут схлопнуться при extraction/map; рекомендация raw/final provenance и rejection до нормализации включена в §§6.2.2 и A9. Публикации являются источником проверяемых предложений, а не подтверждением решения.

## 3. Проблема

Фраза «поддерживает большинство платформ» смешивает четыре независимых свойства: одинаковую семантику, доступность runtime на разных ОС/CPU, пригодность артефакта как библиотеки и изоляцию capabilities. Если сразу назвать один backend переносимым, можно получить работающий бинарник при разной семантике, переносимый bytecode без стабильного API либо sandbox без доказанной связи с owner contract.

Нужен минимальный эксперимент, который разводит эти свойства и проверяет первую полезную ступень фактическим исполнением, а не списком платформ upstream toolchain.

## 4. Цели дизайна
- Сохранить canonical typed IR и generated Dafny source владельцами semantics, независимо от target language.
- Использовать зрелые .NET/JVM toolchains для platform support и machine-code execution через JIT.
- Отделить общий validation proof от target artifacts, не выдавая test owner bundle за human-approved contract и не изобретая скрыто portable admission поверх .NET-specific E05 schema.
- Ввести стабильный logical module interface и явные target ABI adapters.
- Проверять одинаковые outcomes, отказ precondition и artifact identity на фактических Windows/Linux runners.
- Получить данные для решения, оправдан ли следующий прямой Wasm/Component Model backend.

## 5. Non-Goals
- Прямой emitter Core Wasm, WIT component, WASI imports или Wasmtime host.
- Native AOT, GraalVM native image, LLVM, C ABI, Rust crate или JavaScript/browser package.
- Effects, filesystem, network, clock, random, concurrency, dynamic loading и сторонние библиотеки.
- Обещание source/binary compatibility с произвольным C#, Java или Dafny API.
- Поддержка macOS, Arm64, mobile, browser и embedded без actual runner evidence.
- Доказательство корректности Dafny backend, Roslyn/javac, JIT, runtime, OS или CPU.
- Изменение owner semantics, ослабление requires/invariants или перенос target-specific adapter в owner contract.
- Заявление о достижении G05/G06 по latency, throughput, memory или artifact size этого checkpoint.

## 6. Предлагаемое решение (TO-BE)

### 6.1 Распределение ответственности

| Компонент | Ответственность |
| --- | --- |
| Strogo canonical source/typed IR | Единственная identity реализации и platform-neutral semantics |
| Validation owner bundle | Заранее зафиксированный test oracle requires/result/effects/termination; не имеет human `contract-approval.json` |
| Dafny lowering + proof | Общие validation proof obligations до выбора target artifact |
| Dafny C#/Java translators | Target source generation; входят в TCB и pinning |
| Verified wire wrapper | Total `requires true` boundary над closed wire datatype; type/requires checks, structured refusal и candidate call только на valid input |
| Target adapter | Canonical JSON bytes ↔ generated wire datatype и target invocation; никаких owner/candidate вычислительных правил |
| Target build toolchain/runtime | Compilation, loading, JIT и platform services в рамках pure profile |
| Portability manifest | Exact validation artifact, dependency closure, runtime profile и digest; schema/file layout отделены от E05 production package |
| Portability harness | Запуск byte-identical artifact на каждой строке матрицы и comparison с oracle |

### 6.2 Детальный дизайн

#### 6.2.1 Порядок pipeline

```text
fixed validation module + owner bundle
                 │
                 ▼
       canonical typed IR / Dafny source
                 │
         shared proof receipt
            ┌────┴────┐
            ▼         ▼
       translate cs  translate java
            │         │
       .NET adapter  JVM adapter
            │         │
       non-admittable portability manifest per profile
            └────┬────┘
                 ▼
      Windows/Linux portability matrix
```

Proof receipt связывает exact validation module, owner bundle, generated Dafny source и pinned verifier closure. Bundle не имеет `contract-approval.json`, а report обязан показывать `contractStatus: validation-fixture`. Каждый target artifact получает собственный `portability-manifest.v0.1` и запускается только отдельным validation harness. Existing E05 `build-manifest.v0.2` остаётся .NET-specific и не расширяется скрыто; E06 package не содержит production manifest/admission и не передаётся ещё не реализованному production facade. Release admission не создаётся.

Validation proof имеет schema `strogo.validation-proof.v0.1` и ровно поля `schemaVersion`, `contractStatus`, `moduleDigest`, `bundleDigest`, `toolchainDigest`, `closureDigest`, `proofSourcesDigest`, `transcriptDigest`, `sourceMapDigest`, `outcome`, `obligations(obligationId,kind,status,evidenceDigest)`. `contractStatus` равно `validation-fixture`; outcome/status, canonical transcript, sorting, bounded diagnostics и two-clean-replay rules совпадают с E05 proof contract, но поля `contractApprovalDigest` нет. Этот artifact не принимается E05 build/admit/runtime как production proof.

Exact identities используют `H` из §6.2.4:

```text
proofDigest        = H("strogo.validation-proof.v0.1/artifact", canonical full proof)
proofSourcesDigest = H("strogo.validation-proof.v0.1/proof-sources", canonical ordered file inventory)
transcriptDigest   = H("strogo.validation-proof.v0.1/transcript", canonical normative transcript)
sourceMapDigest    = H("strogo.validation-proof.v0.1/source-map", canonical source-map bytes)
evidenceDigest     = H("strogo.validation-proof.v0.1/evidence/<kind>", canonical evidence or null)
toolchainDigest    = H("strogo.validation-proof.v0.1/toolchain", canonical verifier inventory)
closureDigest      = H("strogo.validation-proof.v0.1/closure", canonical transitive verifier/runtime inventory)
```

Каждый inventory состоит ровно из sorted `files(path,sha256,length,role)` и `versions(id,value)`, с теми же path/duplicate rules, что package manifest. `<kind>` — closed obligation kind из proof schema, не agent string.

#### 6.2.2 Execution profiles

| Profile ID | Artifact | Runtime contract | Первая matrix |
| --- | --- | --- | --- |
| `dotnet-managed.v1` | deterministic `net10.0` library + runner files | pinned .NET 10 runtime; no reflection/dynamic load/native dependencies | Windows x64, Linux x64 |
| `jvm-java17.v1` | deterministic JAR + runner manifest | pinned Java 17 runtime; no JNI/reflection/service loading | Windows x64, Linux x64 |

Профиль получает `Portable` только если один и тот же manifest/package/artifact digest triple запущен на обеих ОС. Пересборка на каждой ОС проверяет build portability, но не заменяет этот критерий artifact portability. Dafny proof устанавливает semantics относительно validation owner fixture; фактические runs проверяют сохранение fixed vectors при допущении корректности translator/compiler/runtime TCB. Machine code создаёт соответствующий JIT; E06 не гарантирует correct либо byte-equal JIT output.

Authoritative JVM build не принимает JAR, напрямую созданный `dafny build`, как final artifact. После shared proof он выполняет exact `dafny translate java --no-verify` в isolated clean root, сохраняет translation record, компилирует upstream generated sources pinned `javac --release 17 -encoding UTF-8 -Xlint:all,-cast -Werror` с exact Dafny runtime, а Strogo adapter sources отдельно — `-Xlint:all -Werror`. Исключение `cast` относится только к файлам, перечисленным translator inventory; warning suppression для adapter/consumer запрещён. Любой другой warning/error даёт `TargetBuildRejected`.

JVM packager объединяет resulting candidate/runtime/adapter class entries с exact canonical `META-INF/MANIFEST.MF`, запрещает duplicate/path/tree violations и сначала создаёт raw pre-normalization JAR, который сохраняется в local evidence вместе с ordered central-directory inventory. JAR entry path — relative ASCII с `/`, без empty/`.`/`..` segments, backslash, colon, control, absolute/drive/UNC form; segment соответствует `[A-Za-z0-9_$][A-Za-z0-9._$-]*`; ordinal и case-fold duplicates запрещены. Эти правила отдельны от lowercase outer package paths, потому что Java class/package names case-sensitive.

Canonical manifest имеет exact UTF-8 bytes без BOM: `Manifest-Version: 1.0\r\nAutomatic-Module-Name: strogo.portable.v01\r\n\r\n`. Packager запускает `jar` с working directory, равным isolated staging root. Argfile не содержит physical path или `-C`: первая строка `META-INF/MANIFEST.MF`, затем каждый remaining regular entry ровно один раз в ordinal path order; `.`/directory enumeration запрещены. Canonical logical invocation inventory содержит tool digest, fixed flags, ordered relative argfile entries и их content digests. Physical staging/output paths сохраняются только в diagnostic receipt и исключаются из `buildToolchainDigest`, manifest и semantic report.

Normalizer не использует filesystem extraction. Phase 1 разбирает EOCD, central-directory и local headers в bounded ordered multiset `ordinal,path,compressedLength,uncompressedLength,crc32,method,flags,timestamp,extra,localHeaderOffset` без чтения entry contents. До любого `path → entry` map он проверяет: не более `4096` entries, individual uncompressed length `<=16777216`, aggregate `<=67108864`; один non-ZIP64/non-multidisk archive без encryption/data descriptor/comment; unique non-overlapping local ranges; central/local path, sizes, CRC32, method и flags согласованы; каждый raw entry STORED с flag `0x0800`; path/multiplicity/expected length/manifest rules соблюдены. Ordinal/case-fold duplicate, missing/extra expected resource, changed canonical manifest, directory/symlink-like entry, oversized/deceptive length, overlap или corrupt central/local header дают `CanonicalJarRejected`.

Phase 2 потоково читает каждый raw entry по ordinal напрямую из проверенного local range, без extraction и без map, считает exact `contentDigest`, подтверждает фактическую длину/CRC32 и сравнивает path/content с expected staging inventory. Только после полного PASS разрешено создать unique map и final JAR. Completed phase-2 ordered inventory является input `rawJvmInventoryDigest`. Таким образом duplicate entries с одинаковыми либо разными bytes не могут быть молча сведены к одному dictionary key, а заявленные headers не заменяют проверку содержимого.

Final flags закреплены: `--create --no-manifest --no-compress --date=1980-01-01T00:00:02Z`. Validator требует exact manifest bytes и первый entry; remaining entries — exact ordinal order; directory entries отсутствуют; DOS timestamp каждого entry ровно `1980-01-01T00:00:02`; method — STORED; general-purpose flag — `0x0800`; ZIP create/extract version — `10`, create system — `0`, volume/internal/external attributes — `0`; archive/entry comments отсутствуют; единственный extra field — JAR marker bytes `FE CA 00 00` у manifest; остальные extra fields запрещены. Raw pre-normalization JAR и direct Dafny build JAR остаются intermediate evidence и не входят в package. Два clean normalizations в разных physical roots на canonical Windows x64 build lane обязаны дать byte-equal final JAR и одинаковый `buildToolchainDigest`; Linux rebuild может быть отдельной diagnostic row, но не заменяет запуск этого exact artifact.

Local `jvm-build-receipt.v0.1` сохраняет `rawJvmJarDigest = H("strogo.portability.v0.1/raw-jvm-jar", exact raw bytes)`, `rawJvmInventoryDigest = H("strogo.portability.v0.1/raw-jvm-inventory", canonical ordered-multiset inventory)` и `normalizationRecipeDigest = H("strogo.portability.v0.1/jvm-normalization-recipe", canonical tool digest, fixed flags, exact manifest bytes, relative argfile entries and all validator metadata rules)`. Physical paths отсутствуют во всех трёх digests. Volatile raw digests остаются diagnostic-only и исключены из portability manifest/report semantic projection; exact normalization recipe artifact входит в `buildToolchainDigest`, а raw/final linkage остаётся в local receipt. Tool/logical-argfile/source/class inventories входят в `buildToolchainDigest`/evidence.

#### 6.2.3 Logical module interface и target ABI

Logical interface содержит stable module/function/type IDs, ordered parameters, result type, validation owner revision и closed error union. Dafny lowering дополнительно создаёт общий total wrapper над closed datatype `WireValue` (`WI64`, `WBool`, `WRecord`, `WSeq`) и `WireOutcome` (`Success`, `Refusal`). Wrapper имеет `requires true`, проверяет recursive type/shape/capacity и owner `requires`, вызывает candidate только в доказанно valid branch и возвращает canonical refusal иначе. Proof obligations связывают каждый success с owner model и доказывают, что false-requires/type-invalid paths не вызывают candidate.

Target adapter генерируется детерминированно из typed IR, но отвечает только за transport:

- canonical JSON decimal string ↔ `WI64(MathInt)` через target `BigInteger`; I64 range проверяет wrapper относительно expected signature;
- JSON Boolean ↔ `WBool`;
- record ↔ `WRecord(typeId, ordered fields)` без schema filtering в adapter; exact type/fields/order проверяет wrapper;
- sequence ↔ `WSeq(items)` без element/capacity filtering в adapter; expected element type и `length <= N` проверяет wrapper;
- success ↔ `{ "kind":"success", "value": ... }`;
- refusal ↔ `{ "kind":"refusal", "code":..., "locus":..., "details":... }` с canonical field order.

Adapter обязан строго разобрать canonical JSON в generic `WireValue`, вызвать только verified total wrapper и сериализовать его `WireOutcome`. Adapter не проверяет logical types, record/sequence shape, I64 range или owner `requires`, не вычисляет результат функции повторно и не имеет fallback на reference evaluator. До кодирования adapter проверяет, что target string является последовательностью Unicode scalar values: непарный UTF-16 surrogate даёт `InvalidUnicode`. Только после этого strict UTF-8 encoder без replacement считает byte length; input `>65536` bytes даёт `InputTooLarge`. Malformed JSON, duplicate/unknown envelope property, nesting `>32` или aggregate JSON nodes `>2048` дают transport refusal из закрытого adapter contract до создания `WireValue`; такие refusals входят в differential tests, но остаются TCB. Root JSON value имеет depth `1`, каждый child object/array/scalar — depth родителя `+1`, property name depth не увеличивает. Каждый JSON value node, включая root object, arrays, objects и scalar values, считается один раз; property names не считаются. Любая невозможность представить type/error в target ABI — compile-time `UnsupportedPortableAbi`, а не молчаливое изменение.

Библиотечность проверяют два target-native consumer, отделённые от compiler и harness: standalone C# project компилируется только против packaged `.dll`/public adapter surface без `ProjectReference` на Strogo, standalone Java source — только против packaged JAR через classpath. Каждый consumer вызывает `summarize([4,-3,1])`, `headOrZero([])`, valid `echoSummary` и `adjust(true,I64.MAX)`, тем самым наблюдая sequence/record success, lazy branch и owner refusal, и запускается на обеих ОС. Harness сравнивает их canonical output, но consumers не могут ссылаться на owner/reference evaluator.

Public ABI v0.1 имеет ровно по одной exception-free operation на target:

```text
C#:   Strogo.Portable.V01.ModuleApi.Invoke(string canonicalRequestJson) -> string
Java: strogo.portable.v01.ModuleApi.invoke(String canonicalRequestJson) -> String
```

Метод принимает UTF-8/Unicode string, возвращает canonical JSON и не бросает target exception для входов в transport limits. Fatal VM/process failure остаётся `TargetExecutionFailed`. Request envelope имеет ровно поля в указанном canonical порядке:

```json
{"schema":"strogo.invoke.v0.1","functionId":"summarize","arguments":[{"kind":"sequence","items":[]}]}
```

`arguments` содержит wire values в формах `{"kind":"i64","value":"-1"}`, `{"kind":"bool","value":true}`, `{"kind":"sequence","items":[...]}` и `{"kind":"record","typeId":"Summary","fields":[{"fieldId":"sum","value":...}]}`. Numeric value — canonical decimal string без `+`, leading zero и negative zero. Success/refusal envelope имеет schema `strogo.invoke-result.v0.1`, затем `kind`, затем соответственно `value` либо `code,locus,details`; все objects closed, output property order canonical.

Transport refusal priority фиксирован: `NullRequest` → `InvalidUnicode` → `InputTooLarge` → `MalformedJson` → `TransportDepthLimitExceeded` → `TransportValueLimitExceeded` → `DuplicateProperty` → `UnknownProperty` → `MissingProperty` → `UnsupportedInvokeSchema` → `InvalidRoot` → `UnknownWireKind` → `InvalidI64Encoding` → `NonCanonicalTransport`. `NonCanonicalTransport` является последним catch-all для well-formed input, который нарушает canonical property/array/string representation и не попал в более точный code; throw запрещён. В одном классе выбирается ordinal-minimum JSON pointer. После создания generic `WireValue` wrapper применяет `UnknownFunction` → `ArityMismatch` → `RuntimeTypeMismatch` по позиции аргумента/вложенному locus → `OwnerPreconditionFailed`. Adapter сначала валидирует Unicode scalars, затем измеряет strict UTF-8 byte length, использует bounded streaming parse и собирает structural findings только в пределах `65536` bytes, root-inclusive depth `32` и `2048` JSON value nodes по правилам выше.

`locus` transport refusal равен `$` либо canonical JSON pointer. `details` closed: limit errors имеют `actual,max` как decimal strings; duplicate/unknown/missing property — `property`; unsupported schema — `expected,actual`; invalid root — `expected:"object",actual`; unknown wire kind — `kind`; invalid I64 — `value`; остальные transport codes имеют `{}`. Missing `schema`, `functionId`, `arguments`, `kind`, `value/items/typeId/fields/fieldId` получают `MissingProperty` на parent locus с именем property в details.

#### 6.2.4 Portability evidence

Validation package имеет в корне только `portability-manifest.json` и `content/`. Manifest имеет ровно поля:

```text
schemaVersion, purpose, contractStatus, profileId,
moduleDigest, ownerBundleDigest, proofDigest, dafnySourceDigest,
translatorDigest, buildToolchainDigest, runtimeRequirementDigest,
publicApiDigest, entryArtifactPath,
files(path,sha256,length,role), artifactDigest, packageDigest
```

`schemaVersion` равно `strogo.portability-manifest.v0.1`, `purpose` — `validation-only`, `contractStatus` — `validation-fixture`. Allowed roles: `module`, `bundle`, `proof`, `proof-source`, `source-map`, `entry-artifact`, `adapter`, `runtime-dependency`, `metadata`. Один file имеет role `entry-artifact` и совпадает с `entryArtifactPath`; `files` перечисляет каждый regular file под `content/` ровно один раз и сортируется по path. Применяются E05 path/tree rules: lowercase relative UTF-8 path с `/`, segments `[a-z0-9][a-z0-9._-]*`; absolute/drive/UNC, `.`, `..`, backslash, colon, control chars, device names, ordinal/case-fold duplicates, symlink/junction/reparse/ADS и unexpected root entry запрещены.

`artifactDigest = H("strogo.portability.v0.1/artifact-set", canonical files array)`. `packageDigest = H("strogo.portability.v0.1/package", canonical manifest без packageDigest)`. Full manifest identity: `H("strogo.portability.v0.1/manifest", canonical full manifest)`. `H(tag,bytes)=SHA256(UTF8(tag+"\n") || bytes)`. Canonical JSON использует те же strict UTF-8, integer и property-order rules, что E05 artifacts. Package validator сначала проверяет schema/purpose, closed tree и hashes и только затем разрешает validation harness load. Static API/package checks требуют отсутствия E05 root files, `admit/load` operations и ссылок portability project на будущий `TrustedModuleRuntime`.

`content/public-api.json` имеет role `metadata`, закрытую schema `strogo.public-api.v0.1` и содержит exact target signature, пять ordered function signatures, wire value/result schemas, error priority и resource limits §6.2.3; `publicApiDigest = H("strogo.public-api.v0.1/artifact", exact canonical bytes)`. `content/runtime-requirement.json` аналогично содержит VM family/major, allowed dynamic/native features и OS/arch-independent requirements; `runtimeRequirementDigest = H("strogo.portability.v0.1/runtime-requirement/<profileId>", exact canonical bytes)`. OS-specific runtime bytes в эти digests не входят.

Остальные tool/runtime identities:

```text
translatorDigest      = H("strogo.portability.v0.1/translator/<profileId>", canonical translator inventory)
buildToolchainDigest  = H("strogo.portability.v0.1/build-toolchain/<profileId>", canonical compiler/package inventory)
runtimeClosureDigest  = H("strogo.portability.v0.1/runtime-closure/<profileId>/<os>/<arch>", canonical runtime inventory)
```

Inventories используют ту же закрытую форму `files`/`versions`; profile/os/arch — только closed IDs из manifest/row schema. `portability-report.v0.1.json` агрегирует:

```text
schema, moduleDigest, ownerBundleDigest, proofDigest, dafnySourceDigest,
profiles[profileId, translatorDigest, buildToolchainDigest,
         runtimeRequirementDigest, portabilityManifestDigest,
         packageDigest, artifactDigest, buildReproducible,status,reasonCodes,
         platforms[os,arch,osIdentity,kernelIdentity,runtimeVendor,
         runtimeVersion,runtimeClosureDigest,launcherDigest,harnessDigest,
         portabilityManifestDigest,packageDigest,artifactDigest,status,
         reasonCodes,vectorSetDigest,outcomeDigest,stderrDigest]],
comparisonStatus,reasons(scope,code),semanticDigest,createdAtUtc
```

`semanticDigest = H("strogo.portability-report.v0.1/semantic", canonical semantic projection)`. Projection имеет все поля report кроме `semanticDigest`, `createdAtUtc`, platform `stderrDigest` и отдельных diagnostic/performance/JIT attachments; order сохраняется как в schema. Каждый run сохраняет canonical input/output JSONL, exit code и bounded stdout/stderr. `Unavailable` требует machine-readable причины и не считается pass. Projection показывает validation fixture/proof, target manifests, matrix rows, отсутствие contract approval/release admission и точные границы утверждения.

Profiles сортируются по `profileId`, platform rows — по `(os,arch)`. Row status имеет closed values `Passed`, `Failed`, `Unavailable`; profile status и `comparisonStatus` — `Portable`/`NotPortable`. Profile `reasonCodes` — unique ordinal-sorted closed strings; aggregate `reasons` — unique objects, отсортированные по `(scope,code)`, где scope имеет форму `profile/<id>` либо `platform/<id>/<os>/<arch>`. Closed reason set: коды §6.2.6 плюс `RowFailed`, `RowUnavailable`, `BuildNotReproducible`, `JitEvidenceMissing`, `ConsumerFailed`, `OracleMismatch`; unknown code делает report invalid. `comparisonStatus=Portable` только если оба mandatory profiles имеют по двум `Passed` rows, clean builds reproducible, JIT/consumer gates и все cross-oracle comparisons PASS. Любой другой итог — `NotPortable`; частичный успех остаётся виден по rows, но не повышает общий статус.

Для G02-supporting evidence отдельные diagnostic runs выполняют не менее `50000` вызовов exact vector `summarize([4,-3,1])`, запрещают interpreter-only mode, отключают inlining поддержанным pinned-runtime способом и сохраняют bounded JIT compilation events для fully-qualified `ModuleApi.Invoke` и generated `summarize` method. Exact target symbols берутся из source map, связанной с `moduleDigest`/`dafnySourceDigest`, до запуска. Harness проверяет package tree/digest, связывает оба events с тем же process receipt и выполняет self-tests с decoy symbol и удалённым candidate event; они обязаны дать `JitEvidenceMissing`. Конкретные .NET/HotSpot flags принимаются только если pinned runtime перечисляет/принимает их и они записаны в receipt. Performance runs используют обычные profile settings и не смешиваются с diagnostic run. JIT evidence подтверждает фактическое прохождение этих методов через JIT в конкретном run, но не корректность или byte stability созданного машинного кода; log не входит в semantic digest.

#### 6.2.5 Conformance vectors

Workload фиксируется этой SPEC до EXEC, чтобы нельзя было выбрать удобный пример после знакомства с backend:

```text
record Summary { sum: I64, negativeCount: I64, echo: Seq<I64,8> }

summarize(items: Seq<I64,8>) -> Summary
requires forall item in items: -1000000 <= item <= 1000000
result = left fold from Summary(0,0,[]):
  sum = accumulator.sum + item
  negativeCount = accumulator.negativeCount + if item < 0 then 1 else 0
  echo = seq.append(accumulator.echo,item)

increment(x: I64) -> I64
requires x < I64.MAX
result = x + 1

adjust(increase: Bool, x: I64) -> I64
requires !increase || x < I64.MAX
result = if increase then increment(x) else x

headOrZero(items: Seq<I64,1>) -> I64
requires true
result = if seq.length(items) == 0 then 0 else seq.get(items,0)

echoSummary(summary: Summary) -> Summary
requires true
result = summary
```

`summarize` использует owner root fold, record operations, bounded sequence, Bool comparison и lazy `if` внутри step. `increment` является отдельной public owner entry/model в том же validation bundle; `adjust` является отдельной public owner entry/model и проверяет local call к уже доказанной `increment`. Это не helper contract внутри fold. `headOrZero` даёт observable lazy-branch oracle: eager evaluation невыбранного `seq.get` падает на empty input. `echoSummary` делает record-input ABI наблюдаемым, включая nested sequence. Validation binder допускает только acyclic call к entry того же bundle, proof выполняется в topological call order и связывает call с exact callee ensures; missing entry, cycle или call в fold step остаются отказами. Candidate source может быть выбран агентом, но пять public signatures, owner meanings, bounds и vector set этой секции неизменны.

Exact mandatory vectors:

- `summarize`: `[] → {0,0,[]}`, `[4,-3,1] → {2,1,[4,-3,1]}`, `[1000000×8] → {8000000,0,[1000000×8]}`, `[-1000000×8] → {-8000000,8,[-1000000×8]}`;
- `adjust`: `(false,I64.MAX) → I64.MAX`, `(true,-1) → 0`, `(true,I64.MAX-1) → I64.MAX`;
- `headOrZero`: `[] → 0`, `[7] → 7`; eager-branch mutant обязан завершиться mismatch/crash на empty;
- `echoSummary`: `{2,1,[4,-3,1]} → {2,1,[4,-3,1]}`;
- owner refusal: `summarize([1000001])`, `summarize([-1000001])`, `adjust(true,I64.MAX)`;
- wire/shape refusal: wrong parameter type, missing/extra/reordered record fields, over-capacity sequence of 9 items, null input, malformed JSON, duplicate/unknown/missing property, wrong schema, array root, unknown wire kind, I64 strings `"01"`/`"x"` и noncanonical property order;
- exact resource boundaries: canonical request с ASCII `functionId`, дополненным до total strict UTF-8 length `65536`, проходит transport limit и затем даёт `UnknownFunction`, а вариант length `65537` даёт `InputTooLarge`; request с первым argument из `29` вложенных JSON arrays и deepest `false` имеет root-inclusive depth `32` и доходит до `InvalidRoot`, с `30` arrays/depth `33` даёт `TransportDepthLimitExceeded`; request с `2044` значениями `false` в `arguments` имеет ровно `2048` JSON value nodes и доходит до `InvalidRoot`, с `2045`/`2049` nodes даёт `TransportValueLimitExceeded`;
- Unicode boundary: direct C#/Java invocation со string, содержащей lone high surrogate `U+D800`, даёт `InvalidUnicode`; тот же surrogate вместе с более чем `65536` ASCII bytes всё равно даёт `InvalidUnicode`, подтверждая priority до byte count;
- target mutations: flip sign of sum, reverse input sequence, force eager `headOrZero` branch, alter one refusal code. Comparison must detect each.

Additional generated boundary vectors may be added, but cannot replace or weaken this set.

Expected outcome вычисляется независимым owner model evaluator. Reference evaluator служит второй диагностической линией. Совпадение двух target backends между собой не считается достаточным, потому что они могут разделять ошибочное lowering.

#### 6.2.6 Ошибки и fail-closed policy

| Условие | Результат |
| --- | --- |
| Missing runtime/tool/hash | `EnvironmentUnavailable`, matrix row не пройдена |
| Unsupported source feature/ABI | `UnsupportedPortableAbi`, target artifact не создаётся |
| Translator/compiler warning promoted by profile | `TargetBuildRejected` |
| Requires false/type mismatch | Canonical runtime refusal до candidate invocation |
| Target crash/timeout/noncanonical output | `TargetExecutionFailed` |
| Outcome/error mismatch | `BackendSemanticMismatch`, весь profile не допускается |
| Artifact/build drift | `ArtifactIdentityMismatch` / `NonReproducibleBuild` |

#### 6.2.7 Производительность

EXEC сохраняет diagnostic startup time, warmed throughput, peak working set/RSS и artifact bytes отдельно для C#/.NET и Java/JVM, с фиксированным vector batch и минимум пятью измеряемыми повторениями после двух warmup. Эти числа помогают выбрать следующий backend, но не сравниваются напрямую как доказательство G06: языки, JIT и adapters различаются, а representative workload пока один.

#### 6.2.8 Trusted computing base

| Layer | Trusted components | Binding / evidence | Не доказано |
| --- | --- | --- | --- |
| Validation proof | Strogo parser/canonicalizer/compiler, owner parser/binder/evaluator, Dafny lowerer и wire-wrapper generator, Dafny/Boogie/Z3, canonical JSON/hash code | repository source manifest; pinned tool/archive/executable/closure digests; two clean proof transcripts | Correctness этих реализаций и эквивалентность fixture человеческому намерению |
| Target semantics | Dafny C#/Java translators и target runtimes, Roslyn/javac/package tools, generated wrapper, JSON adapters, .NET/JVM JIT | translator/build/runtime digests, closed package tree, public API digest, platform receipts, oracle differential/mutations | Compiler/JIT correctness и все inputs вне fixed/bounded checks |
| Evidence | Independent owner model evaluator, reference evaluator, portability/package validator, harness/launchers, process output capture, timers/memory counters, SHA-256 implementation | source/tool digests, exact commands, bounded logs, environment identity, repeat runs | Независимость общего source/tool defect; точность OS counters |
| Platform | Windows/WSL Linux kernel, filesystem/process loader, CPU/firmware | OS build/kernel/arch/runtime identity in each row | Полные kernel/firmware hashes, независимое hardware и защита от malicious host |

Отсутствующий digest не заменяется именем версии: row получает `Unavailable`, кроме явно перечисленной ambient platform boundary, где сохраняется доступная build/kernel/CPU identity и ограничивается claim.

Visual planning artifact: текстовая pipeline-схема и platform matrix достаточны для CLI/JSON/Markdown результата. GUI и UI video не применимы.

### 6.3 User-Observable Scenarios

| Scenario | User action / trigger | Expected visible result / output | Evidence required | Covered by AC |
| --- | --- | --- | --- | --- |
| U1 | Собрать fixed validation pure module | Два validation artifacts с общим owner/proof, target manifests и явной non-admittable маркировкой | manifests, projection | A1–A4 |
| U2 | Запустить .NET package в Windows и Linux | Один manifest/package/artifact triple, разные pinned runtime closures, одинаковый canonical outcome digest | two run receipts | A5 |
| U3 | Запустить JVM package в Windows и Linux | Один manifest/package/artifact triple, разные pinned runtime closures, одинаковый canonical outcome digest | two run receipts | A5 |
| U4 | Передать invalid input | Одинаковый structured refusal до candidate invocation | negative JSONL/traces | A6 |
| U5 | Повредить adapter/artifact/manifest или запросить production operation | Validation profile fail closed; production operation отсутствует, package явно `NotAdmittable` | mutation/API evidence | A4,A7–A9 |
| U6 | Не иметь runtime для строки | `Unavailable` с причиной; строка не отображается как поддержанная | report/projection | A10 |
| U7 | Посмотреть показатели | Диагностические startup/throughput/memory/size с environment identity | benchmark report | A11 |
| U8 | Проверить границу заявления о machine code | Candidate entry виден в отдельном JIT diagnostic run, а projection не обещает correct/identical JIT bytes | JIT receipts, docs scan | A12–A13 |
| U9 | Подключить artifact как библиотеку из C# и Java | Standalone target-native consumer собирается только против package public surface и получает success/refusal | consumer build/run receipts | A14 |

### 6.4 State / Interaction Matrix

| Current state | Trigger | Expected transition/result | Empty/error/disabled/concurrent case | Notes |
| --- | --- | --- | --- | --- |
| E05 owner/fold dependencies incomplete | portability build | Refusal `DependencyNotVerified` | Никаких profile artifacts | Не обходить pending approval |
| Shared validation proof verified | build one profile | Portability manifest produced | Другой profile независим | Proof identity общий |
| Validation profile built | запросить admission/load у E06 CLI/API | Операция отсутствует; projection показывает `NotAdmittable` | Только validation harness может запускать artifact | Нет production claim |
| Validation profile built | matrix run | Row receipt bound to exact manifest/package/artifact и OS-specific runtime closure | Missing runtime → `Unavailable` | Не считать upstream support |
| One row failed | aggregate | Profile `NotPortable` | Другие rows сохраняются | Нет partial claim “portable” |
| Repeated same run | rerun | Same semantic outcome digest | Timing may differ | Raw performance не в semantic digest |

### 6.5 Decision Ledger

| Decision | Owner | Default / chosen option | Confidence | Risk if assumed | Needs user before EXEC |
| --- | --- | --- | ---: | --- | --- |
| Первый portability experiment | agent recommendation | Dafny C# + Java backends до прямого Wasm emitter | 0.91 | Меняет прежний порядок, но уменьшает объём собственного backend | Нет; подтверждение этой SPEC и есть решение владельца |
| Платформы | agent | Windows x64 + Linux x64 actual execution | 0.97 | Пока нет Arm/macOS breadth | Нет; граница явная |
| Proof/admission | agent | Один validation owner/proof; E06 manifests не имеют human approval и не проходят production admission | 0.98 | Portable production schema откладывается | Нет |
| Artifact portability | agent | Один byte-identical manifest/package/artifact triple каждого profile на две ОС | 0.92 | Tool-generated package может оказаться nondeterministic | Нет; drift является stop result |
| Java version | agent | Java 17 как зрелый минимальный baseline | 0.89 | Не проверяет новые JVM features | Нет |
| JVM packaging | agent | Separate generated/adapter lint policies + pinned deterministic STORED repack | 0.97 | Direct Dafny JAR содержит volatile timestamps; исключён только known generated `cast` lint | Нет |
| Wasm | agent | Следующий отдельный E06B после ABI/profile evidence | 0.93 | Откладывает sandbox/component experiment | Нет |
| Runtime provisioning | agent | Repo-local/gitignored pinned archives, не system install | 0.90 | URLs/hashes требуют maintenance | Нет |

### 6.6 Runtime / Config / Data Contract Matrix

| Contract area | Current source of truth | Expected change | Compatibility / migration | Verification |
| --- | --- | --- | --- | --- |
| Language semantics | canonical source/IR + owner bundle | Нет | E05 programs unchanged | source/owner/proof digests |
| Proof | generated Dafny source + pinned verifier | Dedicated `validation-proof.v0.1` shared by profiles | E05 production proof schema unchanged and does not accept fixture proof | two clean replays + digest |
| .NET profile | current C# lowering | packaged portable library/adapter | Additive profile ID | Windows/Linux actual run |
| JVM profile | none | Java translation/library/adapter | Additive; no impact on .NET | Windows/Linux actual run |
| Admission | pending E05 two-stage .NET contract | Без изменений; E06 schema/layout/API явно validation-only | Future portable admission требует новой SPEC/version | absence of E05 files/load/admit API + `NotAdmittable` projection |
| Runtime tools | ambient Windows; absent WSL | repo-local pinned runtime archives | `.tools` ignored; каждый platform receipt записывает exact OS-specific runtime closure | installer verify-only + hashes |
| Evidence | ignored local reports + tracked filtered summary | portability report/summary | New versioned schema | canonical parse/hash checks |

## 7. Бизнес-правила / Инварианты

1. `moduleDigest`, `ownerBundleDigest`, `proofDigest` и `dafnySourceDigest` одинаковы для всех profiles одного portability report.
2. `artifactDigest`, translator/build closure и runtime requirement принадлежат profile; exact OS-specific runtime closure принадлежит platform receipt; contract approval и release admission отсутствуют.
3. Ни один platform row не получает `Passed` без actual process execution exact manifest/package/artifact triple; все обязательные rows profile ссылаются на один triple.
4. Profile получает `Portable` только при PASS всех обязательных rows его matrix.
5. Semantic comparison включает kind/value либо code/locus/details; timing, paths и localized stderr в него не входят.
6. Verified total wrapper проверяет logical types/requires и вызывает candidate; adapter проверяет только JSON grammar/resource limits и преобразует representation.
7. Новые effects/imports автоматически выводят модуль из pure portability v0.1.
8. Cross-backend agreement — conformance evidence, а не proof compiler correctness.
9. Любое изменение logical interface, wire wrapper, adapter rules или profile contract требует нового versioned ID и approval; production admission требует отдельной будущей SPEC.

## 8. Точки интеграции и триггеры
- После успешного dedicated validation proof общий receipt с `contractStatus: validation-fixture` становится input profile builder; production E05 `check`, требующий signed contract approval, не вызывается и не подменяется.
- Profile builder вызывает только pinned `dafny translate cs/java`, затем pinned target compiler/package tooling.
- Owner semantics понижается в общий verified total wire wrapper; target adapters не получают отдельный evaluator requires.
- Portability harness запускается отдельной командой, принимает только `purpose: validation-only` и не изменяет admission state.
- Tracked summary создаётся только из complete local report с проверенными hashes.

## 9. Изменения модели данных / состояния
- Новые versioned records: `ExecutionProfileId`, `PortabilityManifest`, `PlatformRunReceipt`, `PortabilityReport`.
- Persistent production state не добавляется. Contract approvals, admissions и E05 production packages не создаются и не меняются.
- Local runtime archives и полные reports находятся в `.tools`/`artifacts/local-validation` и не коммитятся.
- В Git попадают SPEC, implementation, fixtures, schemas/docs и filtered evidence summary без machine-specific абсолютных путей.

## 10. Миграция / Rollout / Rollback
- Изменение additive: текущий C#/.NET lowering и E05 behavior не меняются до явного выбора profile builder.
- Старые module/owner schema не переписываются. Portability доступна только для полного supported pure subset.
- Rollback: удалить additive profile builder/adapters/harness и новые project references отдельным revert; canonical source, owner и proof остаются действительны.
- Если Java backend обнаружит semantic mismatch, сохранить контрпример и оставить только .NET profile; не объявлять весь E05 непригодным без локализации причины.
- Если одинаковые artifact bytes невоспроизводимы, остановить portability PASS и исследовать deterministic packaging отдельной поправкой.

## 11. Тестирование и критерии приёмки

### Acceptance Criteria

- **A1:** обе portability manifests ссылаются на одинаковые exact validation module/owner/proof/Dafny source digests и явно содержат `purpose: validation-only`, `contractStatus: validation-fixture`.
- **A2:** pinned Dafny verifier дважды подтверждает fixed workload §6.2.5 вместе с total wire wrapper и topological exact-entry proof `increment → adjust`; C# и Java translation используют exact verified source bytes.
- **A3:** .NET artifacts собираются с profile warnings-as-errors; JVM generated sources собираются exact `-Xlint:all,-cast -Werror`, adapter/consumer — `-Xlint:all -Werror`; diagnostics равны нулю, а file/dependency/tool/argfile inventories и hashes полны. Попытка применить generated-source `cast` exclusion к adapter/consumer отклоняется.
- **A4:** каждый profile имеет отдельный canonical `portability-manifest.v0.1`; swap/tamper/path-tree mutation отклоняется; package не содержит E05 production files, public E06 API/CLI не имеет load/admit operation, projection равна `NotAdmittable`.
- **A5:** один и тот же `portabilityManifestDigest`/`packageDigest`/`artifactDigest` triple каждого profile фактически исполняется на Windows x64 и Linux x64; receipts отдельно связывают OS-specific runtime closures, а все valid vector outcomes равны owner oracle.
- **A6:** wrong logical type, over-capacity и три fixed false-requires inputs дают canonical equal refusal из verified total wrapper до candidate invocation в обоих profiles/ОС; каждый fixed transport case §6.2.5, включая UTF-8 lengths `65536/65537`, root-inclusive depth `32/33`, JSON node count `2048/2049`, lone surrogate и mixed lone-surrogate/oversize priority, даёт одинаковые exact code/locus/details без target exception.
- **A7:** четыре independent mutations — sign результата, reverse fold input/echo, eager target-code branch `headOrZero` и altered adapter refusal code — каждая обнаруживается как `BackendSemanticMismatch` либо `TargetExecutionFailed` с exact vector/locus.
- **A8:** unsupported effect/type/import, missing call entry, call cycle или call внутри fold step не создаёт partial artifact и возвращает stable refusal/locus.
- **A9:** JVM reproducibility проверяется четырьмя раздельными группами: (a) разные physical roots, input mtimes и input enumeration order при одинаковом expected inventory дают одинаковые final JAR/manifest/build-toolchain digests; (b) допустимое изменение class/resource path или content даёт другой final artifact/package digest; (c) mutation уже выпущенного final JAR в entry order, timestamp, manifest bytes, archive/entry comment, method, flag, create/extract version, system/attributes/volume или extra field всегда даёт `CanonicalJarRejected`, включая вариант с пересчитанными outer manifest/package digests; (d) raw duplicate entry с одинаковыми/разными bytes, missing/extra resource, changed raw manifest, corrupt/mismatched central-local name/size/CRC/method/flags, overlapping range, data descriptor/ZIP64/encryption, `4097` entries, `16777217`-byte entry или aggregate `67108865` bytes даёт `CanonicalJarRejected` в phase 1, а truncated/content-digest mismatch — в phase 2, до extraction/map/final write. Любое нарушение либо неравенство двух остальных clean target builds останавливает EXEC с `NonReproducibleBuild`.
- **A10:** missing/corrupt runtime и absent platform row дают `EnvironmentUnavailable`/`Unavailable`, не PASS.
- **A11:** diagnostic performance report фиксирует команды, warmup/repeats, environment, startup, throughput, peak memory и artifact size без вывода G06.
- **A12:** bounded JIT diagnostics каждого profile/ОС связывают candidate entry method с фактическим compilation event; это не выдаётся за доказательство correctness JIT output.
- **A13:** README/docs/knowledge log после EXEC явно различают validation-fixture proof, отсутствие human contract approval/release admission, backend conformance, actual platform rows, TCB и неподтверждённые platform claims.
- **A14:** standalone C# и Java consumers собираются без ссылок на Strogo compiler/reference projects, только против packaged public surface, и фактически получают exact `summarize`, `headOrZero`, `echoSummary`, `adjust` success/refusal outcomes на Windows/Linux.

### Acceptance-to-Test Matrix

| Acceptance criterion | Automated test | Manual / log check | Evidence artifact | If not tested, why |
| --- | --- | --- | --- | --- |
| A1–A2 | digest binding + verifier replay | inspect source/tool hashes | proof/profile manifests | — |
| A3 | clean target builds, warnings as errors | inventory review | build logs/manifests | — |
| A4 | profile/manifest swap/tamper/path-tree + API/schema scan | projection review | portability/package-boundary report | — |
| A5 | 4 actual process lanes | compare kernel/runtime IDs | platform receipts/JSONL | — |
| A6 | cross-product negative vectors | verify pre-invocation marker | negative receipts | — |
| A7 | four target/adapter mutants | mismatch classification | mutation report | — |
| A8 | unsupported type/effect/import fixtures | no artifact directory/files | refusal report | — |
| A9 | two clean roots + four-group JVM normalization/mutation suite | inspect raw/final inventories and recomputed-outer-digest rejection | reproducibility/normalization report | — |
| A10 | renamed/corrupt runtime fixture | row status review | environment report | — |
| A11 | bounded benchmark harness | inspect environment disclosure | performance JSON/Markdown | G06 intentionally not asserted |
| A12 | .NET/HotSpot JIT diagnostic assertions | inspect bounded method-linked events | JIT receipts | Machine-code correctness intentionally not asserted |
| A13 | docs semantic scan + diff review | boundary review | tracked summary/docs | — |
| A14 | standalone target-native consumer build/run in 4 lanes | inspect dependencies/classpath | consumer receipts | — |

Команды после approval уточняются в EXEC без изменения смысла контракта; минимальный ожидаемый набор:

```powershell
dotnet build Kernel.slnx --no-restore --locked-mode
dotnet run --project tests/Strogo.Modules.Conformance --no-build
pwsh -File tools/Test-Modules-Portability.ps1 -Configuration Release
git diff --check
```

Harness обязан сам проверить prerequisite versions/hashes и запустить Linux process через WSL с repo-local runtime. Timeout ограничивается отдельно для proof, build и каждого platform run; одинаковый timeout не повторяется без новой гипотезы. Отсутствующий runner завершает строку `Unavailable` и весь обязательный profile не-PASS.

## 12. Риски и edge cases

- Dafny semantics доказана, но target compiler может ошибиться; oracle + two-backend differential только снижают риск.
- Dafny target runtimes имеют разные representation и extern surface; pure closed subset и generated adapters ограничивают расхождение.
- JIT/runtime versions влияют на performance и могут влиять на edge behavior при defect; exact closure входит в receipt.
- Java/.NET exception text и stack traces нестабильны; logical type/requires errors формирует verified wrapper, transport errors — closed adapter contract; localized runtime text остаётся только bounded diagnostic.
- WSL использует Linux kernel на той же физической машине; это actual Linux execution, но не независимое hardware/operations evidence.
- Direct Dafny JAR содержит volatile timestamps; final JVM artifact всегда строится exact deterministic repack. Игнорировать whole-file drift, нормализовать неизвестные metadata либо расширять warning exclusions запрещено.
- Raw ZIP/JAR допускает duplicate names; проверка multiplicity обязана идти по central directory до extraction/map, иначе last/first-wins API может скрыть потерю entry.
- Один workload не подтверждает general portability, G05 или G06.

### Expected User Review Objections

| Likely objection | Why likely | Mitigation in spec/code plan | Status |
| --- | --- | --- | --- |
| Почему не Wasm, если он уже выбран следующим | Wasm ближе к sandbox и широкому deployment | C#/Java сначала проверяют отделение semantics/ABI готовыми Dafny backends; Wasm остаётся следующим отдельным E06B | mitigated |
| Java и .NET не дают один native binary | User хочет machine code и платформы | Обе зрелые VM создают machine code через JIT; Native AOT отдельно per RID, без ложного one-binary claim | mitigated |
| Два backend удваивают TCB | Каждый compiler/runtime может ошибаться | Shared validation proof + separate target manifests + owner oracle + differential matrix; TCB раскрыта | accepted-risk |
| Будет ли библиотека удобна обычным C#/Java приложениям | Generated Dafny API нестабилен и неудобен | Stable logical interface + generated native adapters; target ABI versioned | mitigated |
| Не станет ли platform matrix нашей вечной обязанностью | Желание передать portability зрелым платформам | Мы проверяем ограниченный support contract и adapters; OS/CPU/JIT реализуют .NET/JVM vendors | mitigated |
| Где capabilities и sandbox | Это центральное свойство замысла | Pure profile имеет zero imports; capability-bearing WIT/WASI profile проектируется после ABI evidence | accepted follow-up |

### Rework Prevention Checklist
- [x] Видимый результат — два validation artifacts, manifests, standalone consumers и matrix projection — назван.
- [x] Каждый сценарий имеет evidence и AC.
- [x] Решения о порядке backend, platforms, versions и provisioning перечислены.
- [x] Вероятные objections и ограничения гарантий раскрыты.
- [x] Role-based review и отдельный adversarial reviewer pass выполнены после первичного draft; effective reviewer sandbox был writable и раскрыт в §19.
- [x] AC являются проверками результата, включая negative/mutation/unavailable cases.
- [x] EXEC имеет фактические Windows/Linux process lanes и stop rules.

## 13. План выполнения

1. Дождаться approval и завершить E05 owner-composite и fold dependencies; до этого profile builder обязан отказывать. Two-stage admission implementation не является зависимостью.
2. Зафиксировать logical interface/ABI schema и target-independent vector set отдельной контрольной точкой.
3. Реализовать C#/.NET profile, deterministic packaging и Windows/Linux actual execution; сохранить checkpoint evidence.
4. Реализовать Java/JVM profile тем же source/vector contract; выполнить cross-backend/OS matrix и mutations.
5. Добавить standalone consumers, reproducible-build, validation manifest binding, non-admittable package/API checks, diagnostic measurements и tracked filtered summary.
6. Выполнить full regression, knowledge closure и post-EXEC independent review. По результату подготовить отдельную SPEC для portable production package/admission, а затем E06B: Core Wasm + Component Model/capabilities; либо зафиксировать, почему один из этапов не нужен/непригоден.

## 14. Открытые вопросы

Блокирующих approval вопросов нет. Workload, owner meaning и mandatory vectors закреплены в §6.2.5. Exact runtime URLs/hashes выбираются до первого run из официальных vendor artifacts и становятся неизменной частью report identity; смена major/profile требует новой SPEC. Если реализованный E05 не сможет выразить fixed workload, EXEC останавливается и сохраняет этот отрицательный результат вместо упрощения workload или AC.

## 15. Соответствие профилю
- Профиль: `product-system-design`.
- Выполнено: outcome/Non-Goals, boundaries, logical/target contracts, data/evidence, errors, migration/rollback, performance limits, integration, platform state matrix и test plan заданы.
- UI/visual: artifact-facing CLI/Markdown; pipeline diagram и matrix являются достаточным planning artifact, video не применимо.
- Delivery/security: external publication и GitHub delivery исключены; runtime downloads pin/hash; effects/imports запрещены.

## 16. Таблица изменений файлов

| Файл | Изменения после approval | Причина |
| --- | --- | --- |
| `src/Strogo.Modules/**` | profile IDs, logical ABI metadata и fail-closed validation | Общий contract |
| `src/Strogo.Modules.Portability/**` | builders/manifests/adapters | Изолировать target packaging от semantics |
| `tests/Strogo.Modules.Portability.Conformance/**` | vectors, mutations, manifest, non-admittable boundary и standalone consumer tests | Проверяемость A1–A14 |
| `tests/fixtures/portability-consumers/{csharp,java}/**` | Target-native consumers без compiler/reference dependencies | Наблюдаемая библиотечность |
| `fixtures/modules-v0.2/**` | representative module/owner и negative fixtures | Stable test inputs |
| `tools/Install-Portable-Runtimes.ps1` | repo-local pinned .NET/JDK runtimes | Воспроизводимый Linux/Windows runner |
| `tools/Test-Modules-Portability.ps1` | clean builds, 4 lanes, comparison, performance | Один воспроизводимый workflow |
| `Kernel.slnx`, lock/config files | additive projects и pinned dependencies | Build integration |
| `docs/modules-v0.2.md`, `README.md` | фактический profile status и runbook | Human projection |
| `docs/knowledge-log.md` | решения, confirmations/refutations/counterexamples | Обязательное сохранение знаний |
| `artifacts/e06/**` | filtered manifests/report/source inventory | Auditable evidence |
| Эта SPEC | EXEC journal и post-EXEC review | Traceability |

## 17. Таблица соответствий (было → стало)

| Область | Было | Станет после успешного EXEC |
| --- | --- | --- |
| Platform evidence | ReadyToRun win-x64 для E04; E05 Windows development path | Actual Windows/Linux rows для двух managed profiles |
| Backend | Dafny → C# | Один verified Dafny source → C# и Java |
| Library interface | Generated/internal C# consumer | Stable logical interface + versioned C#/Java adapters |
| Approval | E05 proposed two-stage .NET package | E06 не меняет admission; target manifests validation-only, portable admission проектируется по evidence |
| Portability claim | Архитектурный курс | Exact matrix rows с artifact/runtime digests |
| Wasm | Следующий backend без промежуточного ABI evidence | Отдельный E06B после результатов managed profiles |

## 18. Альтернативы и компромиссы

### Сразу прямой Core Wasm backend
- Плюсы: компактная ISA, sandbox foundation, один portable binary format, естественный путь к Component Model.
- Минусы: Strogo сразу владеет новым lowering, memory/error ABI и conformance; stable module interface ещё не проверен на двух существующих ecosystems.
- Решение: сохранить как E06B, сначала проверить platform-neutral semantics и ABI дешевле.

### Только portable .NET IL
- Плюсы: минимальная новая работа, один mature runtime family, один artifact для Windows/Linux.
- Минусы: не проверяет независимость от target language/runtime и может скрыть CLR-specific assumptions.
- Решение: .NET остаётся первым profile, Java создаёт различающую вторую линию.

### Native AOT первым
- Плюсы: standalone machine code, хороший startup и memory profile.
- Минусы: artifact per OS/CPU, ограничения trimming/dynamic features, cross-OS build не универсален; не решает library ABI между ecosystems.
- Решение: будущий deployment profile после managed semantic portability.

### Dafny Rust backend → Wasm
- Плюсы: путь к native/Wasm toolchains и Rust ecosystem.
- Минусы: pinned CLI наличие не является maturity guarantee; в официальном current README Rust не входит в основной перечень target languages; добавляются Rust backend и Wasm binding risks одновременно.
- Решение: не выбирать до отдельного spike с target support evidence.

### Собственный LLVM/native backend
- Плюсы: полный контроль, широкий target set и potential performance.
- Минусы: максимальная собственная ответственность за ABI, lowering, linker/runtime и correctness.
- Решение: не соответствует цели отдать platform support зрелому toolchain на текущем этапе.

## 19. Результат quality gate и review

### SPEC Linter Result

| № | Блок | Статус | Проверяемое основание |
| ---: | --- | --- | --- |
| 1 | A — цель/outcome | PASS | §1 задаёт два package profile, четыре actual rows и canonical report. |
| 2 | A — AS-IS | PASS | §2 отделяет подтверждённые E04/E05 возможности, pinned CLI и environment gaps. |
| 3 | A — проблема | PASS | §3 разводит semantics, runtime portability, library ABI и capabilities. |
| 4 | A — цели дизайна | PASS | §4 фиксирует shared proof, mature runtimes, stable ABI и actual evidence. |
| 5 | A — границы | PASS | §5 исключает production admission, Wasm/AOT, effects и неподтверждённые платформы. |
| 6 | B — ответственности | PASS | §6.1 разделяет source/owner/proof/translator/adapter/runtime/harness. |
| 7 | B — интеграции | PASS | §8 задаёт proof→builder→translator→harness triggers без production loader. |
| 8 | B — алгоритмы/инварианты | PASS | §6.2 и §7 задают wrapper, digest, aggregation, fold/call и portability rules. |
| 9 | B — ошибки/recovery | PASS | §6.2.3/6.2.6 фиксируют closed refusal priority, status/reason и fail-closed results. |
| 10 | B — performance | PASS | §6.2.7 задаёт диагностические метрики, repetitions и границу вывода G06. |
| 11 | C — данные/state | PASS | §6.2.4 и §9 задают closed manifests/reports, local и tracked artifacts. |
| 12 | C — совместимость/migration | PASS | §6.6/§10 сохраняют E05 schemas/behavior и вводят additive profile IDs. |
| 13 | C — rollback | PASS | §10 задаёт revert additive projects и stop paths без ослабления owner. |
| 14 | D — измеримые AC | PASS | A1–A14 содержат exact digests, rows, vectors, statuses и outputs. |
| 15 | D — AC→test/evidence | PASS | Матрица §11 покрывает все A1–A14, включая negative/mutation/unavailable cases. |
| 16 | D — команды/stop rules | PASS | §1/§11 задают bounded commands, prerequisites, timeouts и no-PASS условия. |
| 17 | E — план/dependencies | PASS | §13 требует сначала завершить отдельно утверждаемые owner/fold dependencies. |
| 18 | E — решения/questions | PASS | §6.5 фиксирует agent-owned choices; §14 не оставляет скрытого решения до approval. |
| 19 | E — масштаб/форма | PASS | Large multi-runtime design раскрыт через contracts, matrices, risks и alternatives. |
| 20 | F — профиль | PASS | §15 покрывает `product-system-design`, artifact-facing и delivery/security границы. |

Итог: **ГОТОВО** — FAIL/PARTIAL и незакрытых HIGH/MEDIUM нет.

### SPEC Rubric Result

| Критерий | Балл (0/2/5) | Обоснование |
| --- | ---: | --- |
| 1. Ясность цели и границ | 5 | Наблюдаемый outcome, смена порядка и validation-only Non-Goals явны. |
| 2. Понимание текущего состояния | 5 | Repository, pinned tool help, Windows/WSL environment и official upstream claims разведены. |
| 3. Конкретность целевого дизайна | 5 | Profiles, ABI, wrapper, manifests, digests, statuses и workload заданы нормативно. |
| 4. Безопасность | 5 | E05 admission не меняется; package non-admittable; fail-closed и rollback определены. |
| 5. Тестируемость | 5 | Exact boundary/oracle/mutation/JIT/consumer vectors связаны с A1–A14 и evidence. |
| 6. Готовность к автономной реализации | 5 | Dependencies, этапы, stop rules и agent-owned provisioning decisions зафиксированы. |

Итоговый балл: **30 / 30**. Зона: **готово к автономному выполнению после exact approval и завершения отдельно утверждаемых зависимостей**.

### Role-Based Review Result

| Role | Applicability | Review question | Verdict | Required spec changes |
| --- | --- | --- | --- | --- |
| Business analyst / domain workflow | applicable | Соответствует ли эксперимент целям языка и не выдаёт fixture за человечески утверждённую реализацию? | PASS | Human contract ownership и validation-only boundary закреплены. |
| UX / designer | applicable: CLI/JSON/Markdown artifacts | Понятны ли оператору statuses, причины, ограничения и matrix projection? | PASS | Closed rows/reasons, `NotAdmittable`, scenarios и текстовая схема заданы. |
| Tester / validation | applicable | Различают ли AC ошибки lowering, adapter, target runtime и environment? | PASS | Добавлены fixed workload, four mutations, exact transport boundaries и unavailable rows. |
| Developer / architect | applicable | Связны ли proof, target ABI, package identity, TCB и будущая эволюция? | PASS | Target-independent wrapper и versioned profile contracts отделены от admission. |
| Delivery / operations / security | applicable | Воспроизводимы ли tool/runtime inputs, fail-closed matrix и rollback? | PASS | Pin/hash, closed trees, actual OS rows, local archives и revert path определены. |

### Post-SPEC Review
- Статус / stop decision: **PASS**; exact подтверждение этой SPEC получено, EXEC остаётся dependency-gated.
- Scope reviewed: эта SPEC; central/local `AGENTS.md`; `quest-mode`, `quest-governance`, `spec-linter`, `spec-rubric`, `review-loops`, `product-system-design`; `docs/project-intent.md`; связанные E05 specs и прежний controlled-language experiment; planned files §16; open questions §14.
- Reviewed normative snapshot: SHA-256 `ec491346187203c9a102ec4fcf02c40cdc370355ff373d600c4421dbae608ef3`. После него изменены только согласование provenance в §2, этот audit block, Approval и journal; смысл JVM-контракта не менялся.
- Reviewer boundary: отдельный agent выполнил процедурно read-only pass и не менял файлы, но его effective sandbox был `danger-full-access`; поэтому результат учитывается как writable adversarial fallback, не как технически изолированный read-only independent review.
- Review passes:
  - Scope/Evidence: проверены repo state, related specs/docs, pinned Dafny `translate --help`, available Windows/WSL tooling и пять official upstream sources §2.
  - Contract: outcome/Non-Goals, validation/production boundary, A1–A14, owner/proof/ABI/digest/status contracts и dependencies согласованы.
  - Adversarial risk: проверены ошибочная target admission, скрытый human gate, adapter proof boundary, order/lazy/record counterexamples, false JIT evidence, digest/report ambiguity и Unicode/resource nondeterminism.
  - Role-Based: все пять применимых ролей получили PASS в таблице выше.
  - Fix and re-review: после каждой правки повторно проверялась затронутая поверхность; финальный targeted pass снимка `ec491346…08ef3` подтвердил two-phase JVM parser, exact final metadata и A9 без новых BLOCKER/HIGH/MEDIUM.
  - Stop decision: validation evidence и human approval для E06 достаточны; EXEC остаётся запрещён до отдельных approvals и завершения E05 owner/fold dependencies.
- Evidence inspected: `git status --short --branch`; SHA-256 snapshot; headings/AC scan; Dafny CLI target list; Windows .NET/JDK и WSL inventory; Java translation/javac и two-build raw/repacked JAR inventories; official Dafny README/FAQ/target-library docs, Microsoft Native AOT и WASI releases; Posting Board publish/readback IDs §2.
- Depth checklist:
  - Scope drift / unrelated changes: только эта SPEC; E05/code/docs не изменены.
  - Acceptance criteria: A1–A14 определены один раз и имеют test/evidence rows.
  - User scenarios / Decision ledger / Expected objections: заполнены; user-owned скрытых решений до EXEC нет.
  - Validation evidence: SPEC-level structural/source/environment checks выполнены; runtime evidence честно оставлено EXEC.
  - Unsupported claims: upstream availability не названа Strogo portability; JIT не назван proof correctness; G05/G06 не заявлены.
  - Regression / edge cases: invalid owner/wire/Unicode/resource, tamper, nondeterminism, runtime absence и target mutations покрыты.
  - Comments/docs/changelog: после EXEC обязательны README/docs/knowledge log; до EXEC меняется только SPEC.
  - Hidden contract change: изменение порядка Wasm→managed profiles вынесено в это approval; E05 admission не меняется.
  - Manual-review challenge: наиболее вероятен общий defect shared lowering/oracle; его граница раскрыта в TCB и частично различается independent owner model, reference evaluator, two targets и mutations.
- No-findings justification: финальный reviewer повторно проверил phase-1 header-only bounds/multiplicity/overlap/central-local rules, phase-2 streaming content/CRC/digest rules, exact final JAR metadata и A9 rejection matrix; новых HIGH/MEDIUM findings не обнаружено.

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| HIGH | admission | E05 admission был .NET-specific, а ранний draft подразумевал Java target admission. | Сделать E06 package validation-only и вынести portable production admission в будущую SPEC. | fixed |
| HIGH | proof boundary | Target adapter мог повторно вычислять logical type/requires вне доказанного wrapper. | Ввести total verified `WireValue→WireOutcome` wrapper; adapter оставить transport-only. | fixed |
| HIGH | approval | Validation fixture подразумевал новый human contract approval и production facade dependency. | Убрать approval/admission artifacts и operations; зафиксировать `validation-fixture`/`NotAdmittable`. | fixed |
| MEDIUM | workload | Workload можно было выбрать после backend, а sum/count не различали порядок. | Зафиксировать workload в SPEC и добавить ordered `Summary.echo` плюс reverse mutation. | fixed |
| MEDIUM | semantics | Не было различающих lazy-branch и record-input сценариев. | Добавить `headOrZero`, eager mutant и `echoSummary`. | fixed |
| MEDIUM | library ABI | Library claim не имел exact ABI и независимых target-native consumers. | Задать одну versioned operation на target и standalone C#/Java consumers в четырёх lanes. | fixed |
| MEDIUM | artifact identity | Manifest, public API, proof/tool/runtime digests и TCB были недоопределены. | Закрыть schemas, formulas, inventories, tree rules и TCB matrix. | fixed |
| MEDIUM | JIT evidence | Runtime/JIT claim допускал ложный pass по decoy/inlined method. | Связать два exact symbols с process receipt, отключить inlining и добавить self-tests. | fixed |
| MEDIUM | report | Row/profile status, reason codes и semantic digest projection были неоднозначны. | Задать closed statuses/reasons, aggregation и domain-separated semantic projection. | fixed |
| MEDIUM | transport | I64 representation, error priority, result shapes и resource counting могли разойтись между targets. | Использовать decimal string, closed refusal forms, scalar-first UTF-8 rules, root depth/node count и exact boundary vectors. | fixed |
| MEDIUM | JVM build | Direct Dafny JAR оказался timestamp-dependent, а полный javac lint выдаёт generated cast warnings. | Разделить lint policies и закрепить deterministic pinned-JDK STORED repack с exact entry metadata/order. | fixed, re-reviewed |
| MEDIUM | JVM identity | Physical staging path мог загрязнить digest; manifest/order/timestamp metadata были закрыты не полностью. | Сделать argfile relative-only из fixed cwd, исключить diagnostic paths и задать exact manifest/ZIP fields. | fixed, re-reviewed |
| MEDIUM | JVM oracle | Metadata mutation могла пройти A9 только потому, что SHA изменился; raw duplicate мог исчезнуть при extraction/dictionary. | Разделить equivalence/identity/rejection группы, пересчитывать outer digests в negative test и валидировать raw ordered multiset до map. | fixed, re-reviewed |
| MEDIUM | JVM parser | Raw inventory требовал `contentDigest` до чтения entry bytes и не закрывал corrupt header/size boundary. | Разделить bounded header и streaming-content phases; запретить extraction/map до обеих проверок. | fixed, re-reviewed |
| — | targeted re-review | Нет находок в снимке `ec491346…08ef3`. | Дополнительные изменения JVM-контракта не требуются. | PASS |

- Fixed before continuing: все находки таблицы включены в нормативные §§6–12; публичное ошибочное admission-допущение исправлено отдельным Posting Board reply.
- Checks rerun: exact snapshot hash; targeted reviewer pass; A1–A14 uniqueness/reference scan; headings/required sections scan; после audit выполняются whitespace/link/diff checks.
- Needs human: E06 подтверждена; для начала её EXEC отдельно остаются нужны approvals E05 owner-composite и fold specs. Two-stage admission amendment не является зависимостью E06.
- Residual risks / follow-ups: фактического read-only sandbox reviewer нет; WSL runtimes ещё не provisioned; Posting Board может дать новый контрпример; Dafny/target/JIT correctness остаётся TCB; один fixed workload не доказывает общую переносимость.

### Post-EXEC Review
- Не выполнен до approval и EXEC.

## Approval

Подтверждено владельцем 2026-09-07 точной фразой **«Спеку подтверждаю»**.

Подтверждение распространяется на смену порядка portability experiments и E06 v0.1 validation EXEC после завершения owner-composite/fold dependencies. Оно не разрешает E05 EXEC без отдельных approvals, изменение E05 admission schema, production load/release admission, Wasm/WASI/NativeAOT implementation, merge или release. Отдельной фразой в том же сообщении владелец разрешил периодический push сделанных checkpoint commits.

## 20. Журнал действий агента

| Фаза | Намерение / сценарий | Уверенность | Не хватает | Следующее действие | Нужна передача человеку | Фактическое обращение / решение | Объяснение | Артефакты |
| --- | --- | ---: | --- | --- | --- | --- | --- | --- |
| SPEC | Проверить актуальные platform/toolchain возможности | 0.96 | Actual cross-platform runs | Спроектировать bounded experiment | Нет | Пользователь поручил продолжать | Использованы official docs и pinned Dafny help; upstream support не выдан за evidence Strogo | Эта SPEC |
| SPEC | Разделить semantics, runtime portability, ABI и capabilities | 0.94 | Review counterexamples | Full post-SPEC review | Позднее | Пока не запрашивалось | C#/Java profiles проверяют shared proof/ABI; Wasm сохранён отдельным этапом | Эта SPEC |
| SPEC | Устранить конфликт с .NET-specific admission v0.2 и первым human gate | 0.99 | Independent review | Проверить validation/production boundary | Нет | Пока не запрашивалось | E06 использует `contractStatus: validation-fixture` и non-admittable manifest; portable production admission вынесен в будущую SPEC | Эта SPEC |
| SPEC | Запросить внешний counterexample к смене порядка backend | 0.88 | Ответ участника Posting Board | Учесть рациональный ответ при поступлении; review не блокировать ожиданием | Нет | Публичный reply `15979f1f-cdd1-4b92-8bc4-0378238cd281` опубликован и прочитан обратно | Вопрос сформулирован вокруг falsifiable stop criterion, а не поддержки идеи | Эта SPEC, Posting Board thread |
| SPEC | Исправить публичную target-admission формулировку после review | 0.99 | Нет | Сохранить оба provenance ID | Нет | Поправка `17a295a2-9b34-4e12-80a0-b38fc681d1a9` опубликована и прочитана обратно | Не скрывать опровергнутое допущение: E06A validation-only, portable admission — отдельная SPEC | Эта SPEC, Posting Board thread |
| SPEC | Завершить quality gate и adversarial re-review | 0.98 | Только решение владельца | Запросить exact approval | Да | Отдельный reviewer дал PASS снимку `e1786239…62faf`; effective sandbox writable | Все HIGH/MEDIUM исправлены; 20/20 linter PASS, rubric 30/30, все применимые роли PASS; ограничение независимости раскрыто | Эта SPEC §19 |
| SPEC | Проверить Java backend/package до approval | 0.97 | E06 workload и Linux runtime ещё не реализованы | Закрепить build contract и повторить targeted review | Нет | Posting Board новых ответов после correction не содержит; local feasibility выполнялась только во временных каталогах | Java translation/javac проходят; raw JAR timestamp-dependent, entry contents stable, pinned `jar` repack byte-equal; это не выдано за E06 proof/run | Эта SPEC §§2,6.2.2,11,12 |
| SPEC | Опубликовать Java packaging evidence и закрыть targeted findings | 0.98 | Внешний counterexample пока не получен | Повторить exact-snapshot review | Нет | Reply `2873a93d-4ca2-4046-b7f6-81290c502997` опубликован/read back; reviewer нашёл physical-path digest и incomplete validator gaps | Публичный вопрос просит falsifying case; spec теперь отделяет semantic inventory от diagnostic paths и закрывает JAR bytes/metadata mutations | Эта SPEC §§2,6.2.2,11,19 |
| SPEC | Учесть counterexample Помощника архитектора | 0.99 | Реализация normalizer ещё не существует | Добавить raw/final provenance и rejection fixtures | Нет | Posting Board reply `0cb1efdf-babc-44df-beab-83fe60020411` (#9524) прочитан; duplicate-entry behavior отдельно воспроизведён автором ответа | Ordered multiset проверяется до extraction/map; raw/final inventories и normalization recipe получают domain-separated digests | Эта SPEC §§2,6.2.2,6.2.4,11,12,19 |
| SPEC | Закрыть targeted JVM review и получить решение владельца | 0.99 | E05 owner/fold approvals и EXEC evidence | Зафиксировать checkpoint и продолжить с первой зависимостью | Да | Reviewer дал PASS снимку `ec491346…08ef3`; владелец подтвердил SPEC точной фразой и отдельно разрешил периодические push | Two-phase parser и A9 закрывают найденные обходы; approval не снимает dependency gate | Эта SPEC §§6.2.2,12,19, Approval |
