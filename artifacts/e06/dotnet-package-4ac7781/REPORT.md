# Actual .NET package checkpoint `4ac7781`

Статус checkpoint: **не принят как финальное reproducibility evidence**.

На clean public commit `4ac7781a71d1386d0eb82a4f37b3ba059dd5611f` успешно выполнены:

- mixed proof: два strong runs `42 verified / 0 errors`, weak discriminator `41 / 1`;
- две сборки в sibling roots нативной Linux filesystem: byte-equal translation, record и DLL;
- два byte-equal validation-only package roots с `15` content files и полным `strogo.validation-proof.v0.1`;
- validation-before-invocation и full consumer run одного package на Linux x64 и Windows x64;
- fail-closed проверка неверного внешнего manifest digest без создания staged DLL или validation report.

Идентичности этого package:

- portability manifest: `4620ca92a5f766453af27394869e7c1e6d65b25d81011aaf2b065ec97bb3e673`;
- package: `8cd4093f4d8d7a9a9a96376943de2f24a28762dcb96622c3780e92bab12d8e4d`;
- artifact set: `01804c487e934d4922ee42e6f3df165e2083c8f644e8ccbb7ceb472cb73d92cc`;
- validation proof: `6844985b370593862a5691064ce195d1663d15ef26d54746665acec6381df0a5`;
- entry DLL: `653e97f7012d31ca8489a8f4968720d6efad934463fb8815497751c5459b5d39`.

После run DLL сравнена с ранее сохранённым artifact, собранным внутри Windows-mounted repository tree при тех же candidate, adapter, project, translation record, Dafny и .NET SDK inputs. Ранее получена DLL `37cb02dc8be37fe005d6515421aaba97b069168e70f52d9e8ba2ab019fe314fc`. Первичная гипотеза указывала на абсолютный `build_property.ProjectDir` в generated editor config, но его отключение не устранило drift. Diagnostic Csc command показал достаточную причину: build внутри repository tree унаследовал `Directory.Build.props` и `/checked+`, а native root вне repository получил `/checked-`. Также различался автоматически дополненный `PathMap`.

Поэтому этот каталог сохраняется как отрицательный cross-root checkpoint. Он подтверждает proof/package/двухплатформенный functional path, но не закрывает E06 reproducible-build gate и не переводит `dotnet-managed.v1` в статус `Portable`. Исправление явно фиксирует overflow checks, отключает generated MSBuild editor config и запрещает ancestor `Directory.Build.*` imports; следующая пара builds должна находиться в разных filesystem и ancestor contexts.

Linux evidence получено в WSL2 на Windows host. Оно не является независимым Linux CI или проверкой другого physical host.
