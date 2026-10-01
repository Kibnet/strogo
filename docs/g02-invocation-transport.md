# Строгий transport вызова G02

Компонент `G02InvocationCodec` internal: получает доверенный IR и dispatch callback. Он обеспечивает JSON-проверку и канонизацию, но не открывает пакеты, не проверяет owner state/подпись и не выполняет admission. `G02CompiledDispatch.InvokeJson` вызывает через него тот же типизированный путь Q→F. Это часть будущего публичного runtime, а не готовый допуск программ.

Запрос содержит только `schemaVersion`, `functionId`, `arguments`:

```json
{"schemaVersion":"strogo.invoke.v0.1","functionId":"addOne","arguments":[{"parameterId":"x","value":{"type":"I64","value":"41"}}]}
```

Каждый объявленный параметр IR export указывается ровно один раз. Порядок аргументов и record fields не влияет на результат: значения связываются по ID, затем передаются в порядке сигнатуры. Канонический request сортирует именованные аргументы и поля ordinal; JSON property order/пробелы нормализуются. По transport нельзя выбрать произвольный CLR type/method или путь.

Значения используют owner typed value format: I64 — каноническая десятичная строка в диапазоне signed64, Bool — JSON boolean, Seq — явные kind/elementType/capacity и массив typed values, Record — имя типа и полный массив fieldId/typed value. Значения `-0`, `+1`, `01`, JSON numbers вместо I64 strings отвергаются; ничего не приводится неявно. Все вложенные значения проверяются относительно типов IR. Ёмкость последовательности должна совпадать с декларацией, максимум256.

Пределы:65536 UTF-8 bytes запроса/ответа, JSON depth32,4096 typed value nodes на весь запрос, а не на отдельный параметр. Invalid UTF-8, непечатные/non-ASCII строки, дубли JSON properties, comments/trailing comma, лишние/отсутствующие поля и ошибочные IDs отвергаются до dispatch. В существующем подробном typed format byte limit доминирует над typed node limit для очень больших запросов; shared node counter сохраняется как дополнительный инвариант.

Результат канонический:

```json
{"functionId":"addOne","schemaVersion":"strogo.invoke-result.v0.1","status":"Returned","value":{"type":"I64","value":"42"}}
```

Отказ: ровно schemaVersion,functionId,status=Refused,error={stage,code,entityId}. functionId=null, если корректный ID не был прочитан. Transport stage — invoke; основные codes: SchemaInvalid,SchemaVersionMismatch,DuplicateField,InvalidUtf8,TransportLimitExceeded,DuplicateArgument,ArgumentsMismatch,InputTypeMismatch,InvalidI64,InvalidCapacity,SequenceCapacityExceeded,RecordFieldMismatch,DuplicateRecordField,ValueLimitExceeded. Unknown export — CompiledExportMissing; существующие Compiled* dispatch errors сохраняют исходный stage/code.

Output отдельно проверяется рекурсивно: фактическое имя record, полный набор полей, тип каждого поля/элемента и capacity. Передача ожидаемого type в кодировщик не заменяет эту проверку. Ошибочный output — CompiledOutputMismatch; превышение byte/node/JSON-depth budget — OutputLimitExceeded. Эти выходные отказы происходят **после вызова**, они не означают отсутствие исполнения.

Package/admission/function identities и свежие state gates должен добавлять будущий verified runtime envelope. Этот transport сам по себе не подтверждает G02/AC3–4, полное покрытие compiled ABI или человеческое утверждение смысла. Тесты Bool/record inputs со spy проверяют transport; actual retained generated fixtures отдельно проверяют scalar/allocation JSON path с native Q/F traces. Forced-JIT file identity не является native R2R instruction provenance.
