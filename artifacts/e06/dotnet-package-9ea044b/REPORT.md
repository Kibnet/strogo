# Actual .NET package checkpoint `9ea044b`

Статус checkpoint: **не принят как финальное proof/package identity evidence**.

На clean public commit `9ea044bec776191feb9655124d74b57b155c1104` успешно выполнены:

- mixed proof: два strong runs `42 verified / 0 errors`, weak discriminator `41 / 1`;
- cross-root build: native Linux root вне repository и Windows-mounted root внутри repository дали byte-equal translation, record и DLL;
- checked-semantics DLL SHA-256 `37cb02dc8be37fe005d6515421aaba97b069168e70f52d9e8ba2ab019fe314fc`, length `189440`;
- два byte-equal validation-only package roots с `15` content files;
- validation-before-invocation и consumer `8+24+1+13` одного package на Linux x64 и Windows x64;
- неверный внешний manifest digest дал canonical `ArtifactIdentityMismatch`, exit `1`, без staged artifact и validation report.

Package identities этого запуска:

- portability manifest: `06a6daf1a1b9179c5ebfad3d04c18cc2e712130be0d48fb531bdde378c78f810`;
- package: `39d5bf48234b324db1a52bbe7fd6f36a0637fd6e56766082a6143dabdbffa998`;
- artifact set: `668f3e4e4ddcb9723de98a7f6ffd04bc84dc2f7679e414aea5cb500fb2e9ad4f`;
- validation proof: `74e6287e8359c2a068150df8dc4d5363958bf352528933742298b8acb462ce49`.

После run сравнение с package `4ac7781` показало, что full proof identity зависит от выбранного `--dafny-root`: canonical distribution root и его parent перечисляют те же `290` files с теми же bytes, но второй добавляет logical prefix `dafny/`. Из полей `proof.json` изменился только `closureDigest`, затем изменился domain hash всего proof.

Поэтому этот пакет сохраняется как положительное functional и отрицательное identity evidence. Он подтверждает исправление ambient MSBuild drift, фактическую package validation и двухплатформенный запуск, но не закрывает canonical proof/package gate. Harness исправлен так, чтобы executable был прямым ребёнком exact closure root; требуется новый exact clean-commit package run.

Linux evidence получено в WSL2 на Windows host. Оно не является независимым Linux CI или проверкой другого physical host.
