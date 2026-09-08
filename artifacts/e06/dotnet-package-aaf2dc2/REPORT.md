# Canonical actual .NET package checkpoint `aaf2dc2`

Статус checkpoint: **принят для actual .NET proof/package и двухплатформенного functional path**.

На clean public commit `aaf2dc2a48e99c01cace4ab9d12b4f90a83ffcea` получено:

- mixed proof: два strong runs `42 verified / 0 errors`, weak discriminator `41 / 1`;
- canonical Dafny closure: `290` files, closure digest `4b70255b7602ac191de0101a9dce93719366deeb6f2830db46efdbcd2ef53d23`;
- full validation proof digest `6844985b370593862a5691064ce195d1663d15ef26d54746665acec6381df0a5`, совпадающий с canonical run `4ac7781`;
- native Linux root вне repository и Windows-mounted root внутри repository дали byte-equal translation, record и checked-semantics DLL;
- entry DLL SHA-256 `37cb02dc8be37fe005d6515421aaba97b069168e70f52d9e8ba2ab019fe314fc`, length `189440`;
- два byte-equal validation-only package roots с `15` content files;
- один exact package прошёл validation-before-invocation и standalone consumer `8+24+1+13` на Linux x64 и Windows x64;
- wrong expected manifest и broader Dafny closure root дали canonical refusals с exit `1`, без staged/package/receipt outputs.

Идентичности package:

- portability manifest: `f947db0e3437cb98babe422c6831fa37570b04fd18a8847e12b4620a0119bb8d`;
- package: `4dd1a36f3b430835a0b13241e7bb98a60df451a19befbbbde1801de128d8704e`;
- artifact set: `002c89b04032d9416894f4555ff0bf279be60392ad7ee31b2521ab67840fb08a`;
- build toolchain: `b10ea3836c4228d4443ac69f779902b3b51414cb4955266ee490a0d26e1bb33d`.

Этот checkpoint закрывает actual package binding и functional portability для текущей .NET матрицы. Он не присваивает профилю итоговый статус `Portable`: ещё отсутствуют runtime closure digests, missing-runtime row, performance/JIT evidence A10–A12, JVM profile и общий comparison report. Package имеет статус `validation-fixture` и не является production admission.

Linux evidence получено в WSL2 на Windows host. Оно не является независимым Linux CI или проверкой другого physical host.
