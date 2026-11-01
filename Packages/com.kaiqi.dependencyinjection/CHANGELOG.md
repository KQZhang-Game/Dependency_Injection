# Changelog

## [0.1.0] - 2026-11-01
### Added
- 特性驱动装配（`[Provide]` / `[Inject]` / `[ServiceScope]`）
- Kahn 拓扑排序建图、反拓扑序销毁、构建期环检测
- `Scene` / `Root` 双作用域，Root 跨场景复用
- `IServiceReady` 就绪回调、`IDisposableService` 销毁契约
- 构建期校验（宿主缺少构造注入、工厂与构造函数冲突、Root 依赖 Scene、缺少 `[ServiceScope]`）
- 纯 C# 的 `Runtime` 程序集（`noEngineReferences: true`）与 Unity 适配层分离
