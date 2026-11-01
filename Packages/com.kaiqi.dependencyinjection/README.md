# Dependency Injection

轻量、特性驱动的 Unity 依赖注入容器。核心是纯 C# 引擎（不引用 UnityEngine），Unity 适配层只负责场景对象查找与日志。

## 特性

- 三个特性驱动装配：`[Provide]` 声明服务来源，`[Inject]` 声明依赖点，`[ServiceScope]` 声明作用域
- 支持字段注入、属性注入、方法注入、构造函数注入四种方式
- 依赖图用 Kahn 拓扑排序建立创建顺序，反拓扑序销毁，成环在构建期直接报错
- `Scene` / `Root` 双作用域，仓配器随场景重建，Root 服务跨场景复用
- 装配完成后的 `IServiceReady` 回调，比 `Awake` / `Start` 更准确的事件订阅入口
- 构建期校验：非法配置在 `Initialize` 阶段抛出可读异常，而不是运行到一半才 NullReference

## 安装

本包为 UPM 包。分发方式有三种，按使用场景选择。

**方式一：UPM Git URL（推荐）**

前提：包已推送到公开的 Git 仓库并打了 tag。

1. `Window → Package Manager → + → Add package from git URL...`
2. 填写下面的地址（包位于仓库子目录，必须带 `?path=`）：

```text
https://github.com/<USER>/<REPO>.git?path=/Packages/com.kaiqi.dependencyinjection#v0.1.0
```

也可以直接写进目标工程的 `Packages/manifest.json`：

```json
{
  "dependencies": {
    "com.kaiqi.dependencyinjection": "https://github.com/<USER>/<REPO>.git?path=/Packages/com.kaiqi.dependencyinjection#v0.1.0"
  }
}
```

注意事项：对方机器需要安装 git 并加入 PATH；建议用 tag 锁定版本；升级时要改 tag，或删除 `packages-lock.json` 中对应条目。

**方式二：复制目录（离线兜底）**

把整个 `com.kaiqi.dependencyinjection` 目录（含 `.meta`）复制到目标工程的 `Packages/` 下。

**方式三：本地路径依赖**

在目标工程的 `Packages/manifest.json` 中添加本地路径：

```json
{
  "dependencies": {
    "com.kaiqi.dependencyinjection": "file:../Dependency_Injection/Packages/com.kaiqi.dependencyinjection"
  }
}
```

程序集划分：

| asmdef | 内容 | 备注 |
|---|---|---|
| `DependencyInjection` | Contracts / Attributes / Core | `noEngineReferences: true`，纯 C# |
| `DependencyInjection.Unity` | `Bootstrap`、`UnityRuntimeAdapter` | 依赖 UnityEngine 与 `DependencyInjection` |
| `DependencyInjection.Editor` | 构建期校验测试 | 仅 Editor 平台 |

使用方需要在 asmdef 的 References 里显式引用 `DependencyInjection`；需要 `Bootstrap` 或自定义适配层时再引用 `DependencyInjection.Unity`。`DependencyInjection` 与 `DependencyInjection.Unity` 已设置 `autoReferenced: true`，没有 asmdef 的工程（`Assembly-CSharp`）可直接使用。

## 快速开始

**1. 场景里放引导器**

在第一个场景创建一个空 GameObject，挂上 `Bootstrap`。它标记了 `[DefaultExecutionOrder(-10000)]` 且 `DontDestroyOnLoad`，只需放一次，后续场景不要再放。

**2. 声明服务**

```csharp
[ServiceScope(E_ServiceType.Scene)]
public class ServiceA
{
    public void Initialize() { /* ... */ }
}

// MonoBehaviour 也可以作为 Provider，返回 this 即可
public class Provider : MonoBehaviour
{
    [Provide] public ServiceA ProvideServiceA() => new ServiceA();
}
```

纯 C# 服务也可以用 `[Inject]` 构造函数声明，由容器直接创建：

```csharp
[ServiceScope(E_ServiceType.Scene)]
public class CtorService
{
    [Inject]
    public CtorService(ServiceA serviceA) { /* ... */ }
}
```

**3. 声明依赖**

```csharp
public class ClassB : MonoBehaviour
{
    [Inject] ServiceA serviceA;              // 字段注入
    [Inject] public ServiceB ServiceB { get; set; }  // 属性注入（必须有 setter）

    [Inject]
    public void Init(ServiceA first) { /* ... */ }   // 方法注入

    private void Start() { serviceA.Initialize(); }  // 不要在 Awake 里使用
}
```

注意：新场景加载后，注入发生在 `Bootstrap` 的 `sceneLoaded` 回调里，晚于该场景对象的 `Awake`。因此新场景脚本不要在 `Awake` 中访问 DI 服务，用注入字段并在 `Start` 之后使用。

## 装配时序

```text
Bootstrap.Awake（首个场景）
  └─ Injector.Initialize()
       ├─ 扫描 → 建图 → 拓扑排序
       ├─ Register：按拓扑序创建 Provider / 构造函数服务
       ├─ InjectAll：向场景对象与注入宿主写成员
       └─ NotifyReady：按拓扑序回调 IServiceReady.OnServicesReady

场景切换（Bootstrap 常驻）
  └─ sceneLoaded
       ├─ DisposeSceneServices：反拓扑序调用 Scene 级服务的 IDisposableService.Dispose
       └─ Initialize：为新场景重新创建、注入、Ready（Root 级服务直接复用）
```

## 概念对照

| 特性 | 可标注在 | 作用 |
|---|---|---|
| `[Provide]` | 方法 | 方法返回值即服务，注册键是返回类型 |
| `[Inject]` | 字段 / 属性 / 方法 / 构造函数 | 声明注入点；构造函数注入会让容器负责创建 |
| `[ServiceScope(E_ServiceType.Scene\|Root)]` | 类 | 声明服务作用域，未声明时查作用域会抛异常 |

节点来源（`E_NodeKind`）：

| 来源 | 谁创建实例 |
|---|---|
| `Provider` | 容器调用 `[Provide]` 方法 |
| `Constructor` | 容器调用 `[Inject]` 构造函数 |
| `SceneBehaviour` | 场景里已有，容器只注入不创建 |
| `Unregistered` | 容器不管，需要业务方创建后调用 `InjectExternal` |

Provider 宿主规则：

- MonoBehaviour 宿主：每次装配从场景里查找实例，不缓存（过场景后是新对象）
- 普通类宿主：同一声明类型只创建一个实例，多个 `[Provide]` 共享
- 普通类宿主如果有 `[Inject]` 成员，必须同时声明 `[Inject]` 构造函数，否则构建期报错

作用域规则：

- `Scene` 服务每次过场景销毁重建
- `Root` 服务首次创建后跨场景复用：已存在则跳过创建、不重复注入、不重复 Ready
- `Root` 服务不能依赖 `Scene` 服务，构建期会拦下

## 就绪与销毁

```csharp
public class ReadyService : IServiceReady
{
    // 所有服务创建并注入完成后回调，适合订阅事件、跨服务接线
    public void OnServicesReady() { /* ... */ }
}

public class SceneService : IDisposableService
{
    // 过场景时按反拓扑序调用，依赖方先销毁
    public void Dispose() { /* ... */ }
}
```

`IServiceReady` 的回调发生在 `Bootstrap.Awake` 内部，早于其他 MonoBehaviour 的 `Awake`，因此只应操作 DI 服务。只有服务与注入宿主会被回调；没有任何 `[Inject]` 成员的 MonoBehaviour 不在依赖图里。

## 解析服务

运行期通过注册表按类型取服务：

```csharp
if (Bootstrap.Instance.Registry.TryGetValue(typeof(ServiceA), out var instance))
{
    var serviceA = (ServiceA)instance;
}
```

注册表先查 `Scene` 再查 `Root`；重复注册同一类型会打印警告并覆盖。

手工创建的对象（`Unregistered` 来源）需要注入时，用容器对实例执行成员注入：

```csharp
var host = new UnregisteredHost();
Bootstrap.Instance.Injector.InjectExternal(host);
```

它只处理 `host` 自身类型上的 `[Inject]` 字段、属性与方法，依赖从注册表解析，解析失败直接抛异常；`host` 为 null 时抛 `ArgumentNullException`。`Injector` 由 `Bootstrap` 管理，请勿手动调用它的 `Initialize` / `DisposeSceneServices`。

## 构建期校验

以下配置会在 `Injector.Initialize()` 抛出 `InvalidOperationException`，错误信息会列出具体类型：

- 依赖成环
- 普通类 Provider 宿主有 `[Inject]` 成员但没有 `[Inject]` 构造函数
- 同一类型同时声明了 `[Inject]` 构造函数与 `[Provide]` 工厂
- `Root` 级服务依赖 `Scene` 级服务
- 服务类型与其 Provider 声明类型都没有 `[ServiceScope]`

## 架构

```text
Packages/com.kaiqi.dependencyinjection/
├── package.json
├── README.md
├── Runtime/                       纯 C#，无 UnityEngine 引用
│   ├── DependencyInjection.asmdef
│   ├── Contracts/                 接口、枚举、生命周期契约
│   ├── Attributes/                三个装配特性
│   └── Core/                      Scanner、TypeScanner、Sorter、ServiceRegistry、
│                                  DependencyGraph、Injector、MemberInjector
├── Unity/
│   ├── DependencyInjection.Unity.asmdef
│   ├── Bootstrap.cs               组合根 + 场景生命周期
│   └── UnityRuntimeAdapter.cs     场景对象判定 / 查找 / 日志
└── Editor/
    ├── DependencyInjection.Editor.asmdef
    └── *Tests.cs                  菜单式测试与性能探针
```

引擎与 Unity 的耦合点全部收敛在 `IDependencyRuntimeAdapter` 三个能力上（场景对象判定、场景查找、日志），因此 `Runtime` 可以设置 `noEngineReferences: true`，一旦有人误用 `UnityEngine` 会在编译期失败。自定义环境（编辑器工具、纯逻辑测试）可以替换成自己的适配实现，也可用 `IDependencyScanSource` 注入构造数据来隔离扫描。

扫描范围由组合根决定：`Bootstrap` 传入 `ScannerScanSource.ReferencesDependencyInjection`，只扫描直接引用 `DependencyInjection` 程序集的程序集——要声明特性就必须引用特性类型，所以这个判断不会漏掉合法服务。`Scanner` 静态门面仍保留全量扫描，供性能探针做基线对比。启动日志会打印实际扫描到的程序集数与类型数，范围变化可直接观察。

## 测试

在 Unity 菜单 `Tools/DI` 下按需执行：

```text
Run Dependency Graph Test     依赖图与排序
Run Registration Test         注册与作用域
Run Injection Test            四种注入方式
Run Scene Dispose Test        Scene 级服务销毁顺序
Run Reinitialize Test         过场景重建与 Root 复用
Run Validation Test           构建期校验的异常分支
Measure Reflection Cost       扫描与装配耗时
```

## 已知限制

- `[Inject]` 标在没有 setter 的只读属性上会被静默跳过，请改用字段注入或补 setter
- 场景里的 MonoBehaviour 服务实现 `IDisposableService` 时，过场景清理阶段实例已是 Unity 假 null，`Dispose` 不会被调用，应改在 `OnDestroy` 里释放
- `InjectExternal` 按 `host.GetType()` 查找成员，子类实例不会注入基类上的 `[Inject]` 成员
- 扫描只覆盖直接引用 `DependencyInjection` 的程序集：无 asmdef 的项目依赖框架程序集 `autoReferenced: true` 才能被扫到；运行时通过 `Assembly.Load` 晚加载的程序集也扫不到。扫描结果按扫描源缓存，过场景不重扫
- IL2CPP 构建时被反射访问的成员需要 `link.xml` 保留

## 许可

MIT
