# G02: первая owner-операция допуска

`Strogo.Modules.Owner.Cli approve-contract` создаёт `contract-approval.json` только после проверки подписанного owner state, строгого разбора owner bundle v0.4 и интерактивного ввода **полного** payload digest. Это первая из двух человеческих операций допуска. Команды `admit`, `state advance-epoch`, `check`, `build` и `run` пока не реализованы; текущий checkpoint не запускает модуль.

Оператор заранее размещает вне репозитория RSA public key в DER SubjectPublicKeyInfo, зашифрованный PKCS#8 private key и подписанный `owner-state.json` в защищённом каталоге. `keyId` — lowercase SHA-256 public SPKI bytes. CLI не создаёт ключ и не принимает пароль из аргумента, переменной окружения или файла.

Формат host-owned конфигурации:

```json
{"schemaVersion":"strogo.owner-trust-config.v0.1","keyId":"<64 lowercase hex>","publicKeyPath":"<absolute path to DER SPKI>","ownerStateStore":"<absolute operator-owned directory>"}
```

В `ownerStateStore` должен находиться `owner-state.json`. Формат operator-owned signer конфигурации:

```json
{"schemaVersion":"strogo.owner-signer-config.v0.1","keyId":"<same key id>","publicKeyDigest":"<same key id>","encryptedPrivateKeyPath":"<absolute path to encrypted PKCS#8 PEM>"}
```

Файл provenance содержит ровно `kind`, `reference`, `digest`. Текущая CLI принимает только `kind=git.commit-path-blob`: `reference` имеет вид `<40 lowercase hex commit>:<repo-relative specs/path>:<40 lowercase hex blob>`, а `digest` — lowercase SHA-256 bytes этого Git blob. Запускать команду нужно из корня нужного Git-репозитория. CLI проверяет, что commit существует, указанный путь в нём разрешается именно в заявленный blob, а его bytes соответствуют digest. Проверка выполняется до проекции и повторно перед подписью. Это ссылка на неизменную ревизию SPEC, но сам факт человеческого утверждения этой ревизии оператор проверяет отдельно. Другие виды provenance текущая CLI отвергает. Дата `--valid-until` задаётся явно в UTC как `yyyy-MM-ddTHH:mm:ss.fffZ` и ограничена policy lifetime из signed state.

```powershell
dotnet run --project src/Strogo.Modules.Owner.Cli -c Release -- approve-contract --trust-config <host-owned.json> --signer-config <operator-owned.json> --bundle <bundle.json> --approved-by <id> --provenance <provenance.json> --valid-until <UTC> --out <contract-approval.json>
```

Команда требует терминал для ввода и вывода. Сначала она показывает весь canonical owner bundle, payload будущего approval и `payloadDigest`; затем владелец вводит этот digest без сокращения и пароль ключа без эха. До совпадения digest ключ не открывается. Перед подписью CLI повторно читает state и отказывает при смене его artifact digest. Результат записывается через временный файл и atomic move; существующий `--out` не перезаписывается. Отказ возвращает JSON `status=Refused`, `stage=owner`, `code=...` и ненулевой код выхода.

Conformance с одноразовым fixture key проверяет подготовку/подпись, отказ при неверном digest и ключе, интерактивное создание approval и независимую проверку artifact. Этот fixture не является согласием владельца на D02 bundle или выпуск исполняемого пакета. Требования полного admission и runtime описаны в [G02 SPEC](../specs/2026-09-29-g02-dotnet-r2r-admitted-modules-v0.1.md).
