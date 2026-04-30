# GameplayMessageRouter — 一个游戏内消息广播系统

## 1. 生活中的类比

想象一个**公司的内部对讲机系统**：

- 每个部门（游戏系统）都可以在某个**频道**（FGameplayTag）上广播消息
- 任何感兴趣的人可以**调到那个频道**来收听，不需要知道"谁在说话"
- 你可以精确调到某个频道（`"Security.BuildingA.Floor3"`），也可以调到上级频道监听所有子频道（调到 `"Security"` 就能听到所有 Security 下属频道的内容）
- 广播是**即时的**——你对着对讲机说话，所有在线的人立刻听到

这和一个 Slack 频道或者 Pub/Sub 消息队列本质上是一个东西：**发送者和接收者完全解耦，谁都不用知道对方的存在。**

---

## 2. 结构示意图

```
┌──────────────────────────────────────────────────────────────────┐
│                  UGameplayMessageSubsystem                       │
│                 (每个 GameInstance 一个)                          │
│                                                                  │
│   ListenerMap:  FGameplayTag ──────> FChannelListenerList       │
│                                                                  │
│     "Game.UI.Health"  ──────> [Listener#1, Listener#2]          │
│     "Game.UI"         ──────> [Listener#3]                      │
│     "Game"            ──────> [Listener#4]                      │
│                                                                  │
└──────────────────┬───────────────────────┬───────────────────────┘
                   │                       │
     BroadcastMessage()           RegisterListener()
                   │                       │
                   ▼                       ▼
         ┌─────────────┐        ┌──────────────────┐
         │  发送者 A    │        │   接收者 B         │
         │ (C++ 或 BP)  │        │ (C++ Lambda / BP)  │
         │              │        │                   │
         │ 广播到       │        │ 调到               │
         │ "Game.UI.    │        │ "Game.UI.Health"  │
         │  Health"     │        │ 频道              │
         └─────────────┘        └──────────────────┘
                 ╲                      ╱
                  ╲                    ╱
              "互相完全不知道对方的存在"
```

### 消息匹配（层级遍历）

```
发送者广播到 "Game.UI.Health.CurrentHP"

遍历标签层级:
                                  ┌──────────────────────┐
  "Game.UI.Health.CurrentHP" ──►  │ 精确匹配 和 部分匹配  │ ← 第一个标签（初始标签）
                                  │ 都会被触发！          │
                                  └──────────────────────┘
           │
    RequestDirectParent()
           │
           ▼
  "Game.UI.Health" ───────────►  ┌──────────────────────┐
           │                      │  只有 PartialMatch    │ ← 父级标签
           │                      │  监听器被触发         │
    RequestDirectParent()         └──────────────────────┘
           │
           ▼
  "Game.UI" ──────────────────►  ┌──────────────────────┐
           │                      │  只有 PartialMatch    │
    RequestDirectParent()         │  监听器被触发         │
           │                      └──────────────────────┘
           ▼
  "Game" ─────────────────────► ... 以此类推，直到根标签
```

---

## 3. 逐步走读代码

整个插件分两个模块，我们按"发消息→收消息"的流程走一遍。

### 第一步：获取子系统

**文件**: `GameplayMessageSubsystem.h`

```cpp
UCLASS(MinimalAPI)
class UGameplayMessageSubsystem : public UGameInstanceSubsystem
```

它是一个 `UGameInstanceSubsystem`，这意味着**每个 GameInstance 自动创建一个实例**，生命周期和游戏进程一样长（跨关卡切换不会丢失）。

获取它的方式是静态方法 `Get()`：

```cpp
static UGameplayMessageSubsystem& Get(const UObject* WorldContextObject);
```

实现中做了两跳（`GameplayMessageSubsystem.cpp`）：
1. 通过 `GEngine->GetWorldFromContextObject` 找到当前 World
2. 通过 `World->GetGameInstance()->GetSubsystem<UGameplayMessageSubsystem>()` 拿到子系统

### 第二步：注册监听器

**文件**: `GameplayMessageSubsystem.h` / `.cpp`

C++ 端提供了三层接口：

| 方式 | 使用场景 |
|------|---------|
| `RegisterListener<FMsg>(Tag, Lambda)` | 最简单的场景，传一个 lambda |
| `RegisterListener<FMsg, TOwner>(Tag, Object, &Func)` | 绑定成员函数，自动用弱指针保护生命周期 |
| `RegisterListener<FMsg>(Tag, Params)` | 需要更精细控制匹配类型时 |

最终都汇聚到 `RegisterListenerInternal()`：

```cpp
// 在 ListenerMap 中找到/创建该 Tag 对应的 FChannelListenerList
// 分配一个递增的 HandleID
// 返回一个 FGameplayMessageListenerHandle
```

`FGameplayMessageListenerHandle` 是一个轻量级 RAII 句柄：
```cpp
// 调用 Unregister() 就从系统中移除监听
Handle.Unregister();
// 或检查 IsValid() 看是否还在监听
```

### 第三步：广播消息

**文件**: `GameplayMessageSubsystem.cpp` — `BroadcastMessageInternal()`

```cpp
// 1. 从发送的 Channel 开始
bool bOnInitialTag = true;
for (FGameplayTag Tag = Channel; Tag.IsValid(); Tag = Tag.RequestDirectParent())
{
    // 2. 查找该 Tag 下注册的监听器
    if (FChannelListenerList* pList = ListenerMap.Find(Tag))
    {
        // 3. 遍历每个监听器
        for (const FGameplayMessageListenerData& Listener : pList->Listeners)
        {
            // 匹配规则:
            //   初始 Tag → ExactMatch 和 PartialMatch 都会被调用
            //   父级 Tag → 只有 PartialMatch 被调用
            if (bOnInitialTag || Listener.MatchType == EGameplayMessageMatch::PartialMatch)
            {
                // 4. 类型安全检查
                // 5. 调用 Listener.ReceivedCallback
            }
        }
    }
    bOnInitialTag = false;
}
```

这里的关键逻辑：
- **向上走**层级树（`RequestDirectParent()`），不是向下
- **初始Tag**既可以匹配精确也可以匹配部分
- **父级Tag**只能匹配`PartialMatch`

### 第四步：蓝图异步节点

**文件**: `AsyncAction_ListenForGameplayMessage.h` / `.cpp`

蓝图用的是一个异步动作节点：

```
[Listen For Gameplay Messages]
    ├── Channel (输入)
    ├── PayloadType (输入)
    ├── MatchType (输入)
    ├── OnMessageReceived (输出执行引脚)
    │       ├── Payload (输出数据)
    │       └── ActualChannel (实际频道)
```

`Activate()` 被调用时，内部注册了一个 lambda：
```cpp
Subsystem->RegisterListenerInternal(Channel,
    [WeakThis](FGameplayTag Channel, const UScriptStruct* StructType, const void* Data) {
        // 保存 payload 指针
        // 广播 OnMessageReceived 委托
        // 如果没有绑定了，SetReadyToDestroy() 自动清理
    });
```

**生命周期自动管理**：当所有绑定到 `OnMessageReceived` 的蓝图节点都被销毁后，异步节点会自动调用 `SetReadyToDestroy()` 来进行自我清理。不需要手动取消注册。

### 第五步：K2Node（蓝图编译器魔法）

**文件**: `K2Node_AsyncAction_ListenForGameplayMessages.cpp`

这是"幕后"的蓝图编译器节点。当你在蓝图中放置 `Listen for Gameplay Messages` 节点时，K2Node 负责：
- 动态创建 `Payload` 输出引脚（类型跟随 `PayloadType` 输入引脚变化）
- 在编译蓝图层时，自动插入 `GetPayload` 的调用节点来提取数据
- 把内部的 `FGameplayTag` 类型转换成蓝图友好的形式

---

## 4. 一个关键陷阱

**同步分发 = 你不能在回调中做重型操作**

看 `BroadcastMessageInternal()` 的实现，它是直接在调用栈中同步调用每个监听器的回调：

```cpp
// 在 BroadcastMessage 调用栈内，直接就调了你的 callback！
Listener.ReceivedCallback(Tag, Listener.StructType, MessageBytes);
```

这意味着：
- ❌ **不要在回调中广播其他消息**——可能导致意外的重入和调用顺序混乱
- ❌ **不要在回调中加载关卡或做耗时操作**——会阻塞整个游戏的这一帧
- ❌ **不要在回调中取消注册其他监听器**——正在遍历 `TArray<FGameplayMessageListenerData>`，修改数组会导致迭代器失效

正确的做法是：
- ✅ 复制你需要的数据到本地变量
- ✅ 用 `AsyncTask` 或 `FTimerHandle` 延迟处理
- ✅ 如果必须取消注册，在回调外用队列/标志位来延迟取消

这就是为什么文档说这是一个"轻量级消息分发系统"——它设计用于快速通知，而不是复杂的消息总线。

---

### 总结

`GameplayMessageRouter` 本质上是一个 **30 行核心逻辑的 pub/sub 系统**：一个 Tag → ListenerList 的 Map，广播时遍历层级树，同步调用回调。它巧妙的地方在于：

1. **复用 GameplayTag 的层级系统**做通道匹配（无需自己实现路径匹配）
2. **UScriptStruct* 运行时类型检查**保证了消息类型安全
3. **一体化生命周期管理**——GameInstance 级子系统 + 弱指针回调 + 异步节点自动销毁
