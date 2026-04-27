# GameFeature 系统详解

## 1. 类比：游戏扩展包（Expansion Pack）

想象你买了一款角色扮演游戏。几个月后，开发商发布了一个"龙之怒"扩展包——安装后，你的游戏世界突然多了龙巢副本、龙的AI、新技能和龙鳞装备。你可以在启动时选择"加载龙之怒"或"不加载"，游戏核心代码完全不变。

**GameFeature 就是 UE 里的扩展包系统。** 每个 GameFeature 是一个独立的"功能包"，可以在运行时被激活（打开）或停用（关闭），核心游戏代码不需要知道它们的存在。

**第二个类比：USB 即插即用。** 插上鼠标，操作系统自动加载驱动、配置输入映射、让光标动起来。拔掉鼠标，一切干净地撤销。GameFeature 对游戏玩法做的事情，就是这个道理。

---

## 2. 架构图

```
┌──────────────────────────────────────────────────────────────────┐
│                     UGameFeatureAction                            │
│                     (UE Engine 基类)                               │
│                                                                   │
│  OnGameFeatureActivating()    ← 扩展包被激活时调用                  │
│  OnGameFeatureDeactivating()  ← 扩展包被停用时调用                  │
└───────────────────────┬──────────────────────────────────────────┘
                        │ 继承
        ┌───────────────▼──────────────────────┐
        │  UGameFeatureAction_WorldActionBase  │  ← 项目的抽象基类
        │  (Abstract)                          │
        │                                      │
        │  激活时：订阅 OnStartGameInstance    │
        │  对已存在的世界：立即调用 AddToWorld() │
        │  新世界创建时：也调用 AddToWorld()    │
        │                                      │
        │  ★ 纯虚函数：AddToWorld()            │
        └───────┬──────────┬──────────┬────────┴───────┬──────────┐
                │          │          │                │          │
    ┌───────────▼──┐ ┌─────▼────┐ ┌──▼──────────┐ ┌───▼───────┐ ┌▼──────────┐
    │ AddAbilities │ │AddInput  │ │AddLevel      │ │AddSpawned│ │AddWorld   │
    │              │ │Mapping   │ │Instances     │ │Actors    │ │System     │
    │ 授予技能+属性 │ │添加按键映射│ │动态加载关卡   │ │生成Actor │ │创建世界系统│
    └──────┬───────┘ └─────┬─────┘ └──────┬───────┘ └───┬───────┘ └─────┬──────┘
           │               │              │              │              │
           ▼               ▼              ▼              ▼              ▼
    ┌──────────┐   ┌──────────┐   ┌──────────┐   ┌──────────┐   ┌──────────┐
    │ ASC组件  │   │Enhanced  │   │Level     │   │World::   │   │World     │
    │ 属性集   │   │Input     │   │Streaming │   │SpawnActor│   │Subsystem │
    │ 输入绑定 │   │Subsystem │   │Dynamic   │   │          │   │(Manager) │
    └──────────┘   └──────────┘   └──────────┘   └──────────┘   └──────────┘
```

**生命周期流程：**

```
  激活（Activate）                          停用（Deactivate）
  ════════════                              ══════════════

  OnGameFeatureActivating()                 OnGameFeatureDeactivating()
        │                                          │
        ├─ 订阅 OnStartGameInstance                ├─ 取消订阅
        │                                          │
        ├─ 遍历已有 WorldContext                    └─ Reset()
        │    └─ AddToWorld(每个World)                    │
        │                                               ├─ 移除技能/属性
        └─ 未来新World创建时                              ├─ 卸载映射
             └─ HandleGameInstanceStart                   ├─ 销毁Actor
                    └─ AddToWorld(新World)                └─ 卸载关卡
```

---

## 3. 逐步解析代码

### 第一层：基类 `WorldActionBase`

文件：`Source/TPGame/GameFeatures/GameFeatureAction_WorldActionBase.cpp`

这是最关键的设计——它解决了 **"世界还没创建时怎么办？"** 的问题。

```cpp
void UGameFeatureAction_WorldActionBase::OnGameFeatureActivating()
{
    // ① 订阅"游戏实例启动"事件，未来有新世界时能自动响应
    GameInstanceStartHandle = FWorldDelegates::OnStartGameInstance
        .AddUObject(this, &HandleGameInstanceStart);

    // ② 遍历当前已经存在的所有世界（可能已经有多个PIE世界在运行）
    for (const FWorldContext& WorldContext : GEngine->GetWorldContexts())
    {
        AddToWorld(WorldContext);  // 纯虚函数，由子类实现
    }
}
```

**两个关键点：**
- **注册未来的世界**：通过委托，确保从现在开始任何新建的世界都会收到 `AddToWorld` 调用。
- **处理已存在的世界**：遍历 `GEngine->GetWorldContexts()`，因为激活时可能已经有运行中的世界。

当新的 GameInstance 启动时：

```cpp
void UGameFeatureAction_WorldActionBase::HandleGameInstanceStart(UGameInstance* GameInstance)
{
    if (FWorldContext* WorldContext = GameInstance->GetWorldContext())
    {
        AddToWorld(*WorldContext);  // 再次调用纯虚函数
    }
}
```

### 第二层：五个具体 Action

#### （1）`AddAbilities` — 最复杂的 Action

文件：`Source/TPGame/GameFeatures/GameFeatureAction_AddAbilities.cpp`

核心思路：使用 `UGameFrameworkComponentManager` 的扩展处理器机制，对**特定 Actor 类型**进行拦截。

```
配置：ActorClass = ACombatCharacter，GrantedAbilities = [Fireball, Heal]

1. AddToWorld() 
   └─ 调用 ComponentMan->AddExtensionHandler(ACombatCharacter, Delegate)
      含义："每当任何 ACombatCharacter 被创建/移除时，通知我"

2. 当 ACombatCharacter 生成时
   └─ HandleActorExtension(Actor, "ExtensionAdded", EntryIndex)
       └─ AddActorAbilities(Actor, Entry)
           ├─ FindOrAddComponentForActor<UAbilitySystemComponent>(Actor)
           │   ├─ 如果Actor没有ASC → 请求 ComponentManager 创建
           │   └─ 如果已有 → 检查是否是 "原生" 组件
           ├─ AbilitySystemComponent->GiveAbility(Fireball)
           ├─ 找到/添加 AbilityInputBindingComponent
           └─ InputComponent->SetInputBinding(右键, Fireball的Handle)
```

`FindOrAddComponentForActor` 有一个微妙的判断逻辑：

```cpp
// 如果组件是"Native"创建方式且其原型是CDO，
// 说明这可能是另一个GameFeature创建的，需要重新请求（引用计数）
if (Component->CreationMethod == EComponentCreationMethod::Native)
{
    UObject* ComponentArchetype = Component->GetArchetype();
    bMakeComponentRequest = ComponentArchetype->HasAnyFlags(RF_ClassDefaultObject);
}
```

这保证了多个 GameFeature 可以安全地共享同一个组件类型而不冲突。

#### （2）`AddInputContextMapping` — 最简洁的 Action

文件：`Source/TPGame/GameFeatures/GameFeatureAction_AddInputContextMapping.cpp`

监听 `APlayerController` 类，每当玩家控制器就绪就把 `UInputMappingContext` 注入到 EnhancedInput 子系统：

```cpp
void UGameFeatureAction_AddInputContextMapping::AddInputMappingForPlayer(UPlayer* Player)
{
    if (ULocalPlayer* LocalPlayer = Cast<ULocalPlayer>(Player))
    {
        if (UEnhancedInputLocalPlayerSubsystem* InputSystem = 
            LocalPlayer->GetSubsystem<UEnhancedInputLocalPlayerSubsystem>())
        {
            InputSystem->AddMappingContext(InputMapping.LoadSynchronous(), Priority);
        }
    }
}
```

停用时反向操作，通过 `RemoveMappingContext` 移除映射。

#### （3）`AddLevelInstances` — 动态加载关卡

文件：`Source/TPGame/GameFeatures/GameFeatureAction_AddLevelInstances.cpp`

通过 `ULevelStreamingDynamic::LoadLevelInstanceBySoftObjectPtr` 在运行时动态加载子关卡，支持位置和旋转偏移：

```cpp
ULevelStreamingDynamic* StreamingLevelRef = ULevelStreamingDynamic::LoadLevelInstanceBySoftObjectPtr(
    TargetWorld, Entry.Level, Entry.Location, Entry.Rotation, bSuccess);
```

激活时额外订阅了 `OnWorldCleanup`，防止世界被清理时留下悬空引用。加载完成后通过 `OnLevelLoaded` 回调将 `LoadedNotVisible` 的关卡设为可见。

#### （4）`AddSpawnedActors` — 最直接的 Action

文件：`Source/TPGame/GameFeatures/GameFeatureAction_AddSpawnedActors.cpp`

激活时 `World->SpawnActor`，停用时遍历 `SpawnedActors` 数组逐个 `Destroy()`。逻辑直截了当，没有复杂的生命周期管理。

#### （5）`AddWorldSystem` — 引用计数的单例系统

文件：`Source/TPGame/GameFeatures/GameFeatureAction_AddWorldSystem.cpp`

通过 `UGameFeatureWorldSystemManager`（一个 `UWorldSubsystem`）用引用计数管理单例 UObject：

```cpp
UGameFeatureWorldSystem* RequestSystemOfType(TSubclassOf<UGameFeatureWorldSystem> SystemType)
{
    // 如果类型的实例已存在，计数器+1
    for (auto& Existing : SystemInstances)
    {
        if (Existing.Key->GetClass() == SystemType)
        {
            Existing.Value += 1;
            return Existing.Key;
        }
    }
    // 否则创建新实例，计数器初始为1
    UGameFeatureWorldSystem* NewSystem = NewObject<UGameFeatureWorldSystem>(GetWorld(), SystemType);
    SystemInstances.Add(NewSystem, 1);
    return NewSystem;
}
```

多个 GameFeature 请求同一个 System 类型时不会重复创建，计数器归零才真正移除。

---

## 4. 容易踩的坑

**坑：GameFeature 激活的时机早于 World 创建**

如果你在 GameFeature 的 `OnGameFeatureActivating()` 里直接操作 World 或 Actor，你很可能会发现世界还不存在。`WorldActionBase` 的设计正是为了解决这个问题——它先订阅 `OnStartGameInstance`，等世界就绪了再通过 `HandleGameInstanceStart` → `AddToWorld` 执行实际操作。

**这就是为什么所有五个 Action 都不重写 `OnGameFeatureActivating` 的核心逻辑，而是只实现 `AddToWorld()`。** 如果你自己写一个新的 Action，也应该继承 `WorldActionBase` 并只实现 `AddToWorld`，而不是直接写在 `OnGameFeatureActivating` 里。

另外，`AddAbilities` 中的 `FindOrAddComponentForActor` 对 "Native" 创建方式的判断是一个脆弱的启发式方法——代码注释自身也承认了这一点——如果多个 GameFeature 同时操作同一个 Actor 类型，组件归属的判断可能出错。
