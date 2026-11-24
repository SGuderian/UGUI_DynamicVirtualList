# Dynamic Virtual List

基于 UGUI 的单轴动态虚拟列表。控制器使用显示索引、累计尺寸和模板 ID 布局，复用在屏单元格。业务脚本维护模型、显示顺序和树的展开状态，并注册单元格回调。

| 模式 | 主轴 | 尺寸规则 |
| --- | --- | --- |
| `VerticalList` | 纵向 | 动态高度，宽度由视口决定 |
| `HorizontalList` | 横向 | 动态宽度，高度由视口决定 |
| `Grid` | 纵向 | 固定列数，行高取该行所有条目的最大高度 |
| `Tree` | 纵向 | 业务提供拍平后的可见序列，按回调返回的深度缩进 |

## 模块

- `Runtime/Scripts/Interfaces` 定义业务使用的 `IVirtualListAdapter` 和布局使用的 `IVirtualLayout`。
- `Runtime/Scripts/Base` 提供 `VirtualListAdapterBase`，管理初始化、回调、滚动、复用和测量。
- `Runtime/Scripts/List`、`Grid`、`Tree` 各自提供控制器和布局。纵向和横向共用 `ListScrollView`，其 Inspector 的 `Horizontal` 控制方向。网格用 `GridScrollView`，树用 `TreeScrollView`。

## 运行示例

在 Unity Project 窗口打开 **Packages → Dynamic Virtual List → Examples → Scenes**，选择任一场景进入 Play Mode。工程已通过本地包路径引用插件，使用 Unity `2022.3.62f2c1` 和 UGUI `1.0.0`。

| 场景 | 验证内容 |
| --- | --- |
| `VerticalList.unity` | 1,200 条数据、随机行高 44–280、池化复用、连续跳转压测 |
| `HorizontalList.unity` | 120 条数据、动态宽度、横向滚动条 |
| `Grid.unity` | 600 条数据、四列、按行聚合高度 |
| `Tree.unity` | 190 个业务节点、点击展开折叠、在业务中重定位视口锚点 |

四个场景都有添加数据、反转显示顺序、跳到末尾和切换循环滚动的按钮。示例预制体在 `Examples/Prefabs`，回调和业务集合在 `Examples/Scripts/DemoDataBootstrap.cs`。
单击普通列表项可选择或取消选择，单击树分支可展开或折叠。场景顶部会显示刷新和滚动状态。

场景底部可以输入从 0 开始的 `Index` 和 `Slot`，测试顶部、底部和指定视口位次跳转。也可以输入 `Offset` 测试坐标跳转，纵向取 Y，横向取 X，单位为 UGUI 布局单位。`Precise` 和 `Smooth` 分别控制精确修正和平滑过渡。

在 VerticalList 保持循环滚动关闭，点击 `Run stress` 执行 96 轮固定种子测试。测试覆盖顶部、底部、视口位次和坐标跳转，也会让新请求覆盖上一条跳转。面板显示通过数、失败数和首条失败的位置误差。再次点击可停止测试。

## 接入业务

1. 在 Unity Hierarchy 右键选择 **DynamicList → Vertical List / Horizontal List / Grid / Tree**。模板带有视口、裁剪区、内容容器和对应方向的滚动条。
2. 选中列表根节点，在适配器的 Inspector 设置 `Default Item Size` 和 `Item Templates`。可为不同类型配置多个 Prefab，每个模板使用唯一 ID。条目 Prefab 根节点需要有 `RectTransform` 和 `VirtualItemCell` 或其派生组件。
3. 在业务组件中引用 `VirtualListAdapterBase`，通过 `RegisterCallbacks` 绑定数据，调用 `RegisterItemTemplateCallback` 按显示索引选择模板 ID。准备好显示序列后调用 `Init(itemCount)`。
4. 改变显示内容、索引顺序、模板 ID、深度或数量后，先更新业务集合，再调用 `RefreshItems`。已绑定 UI 单纯发生尺寸变化时调用 `MarkItemDirty(index)`。
5. 业务退出或释放数据前调用 `ClearCallbacks()`。在回收回调中移除业务为该单元格注册的监听。单元格回收期间仍可读取旧索引。重新启用业务时，再注册并初始化。

```csharp
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using VirtualList;

public sealed class InventoryList : MonoBehaviour
{
    [SerializeField] private VirtualListAdapterBase _view;
    private readonly List<(string Title, int TemplateId)> _items = new List<(string, int)>
    {
        ("Potion", 0),
        ("Key", 1)
    };

    private void OnEnable()
    {
        IVirtualListAdapter adapter = _view;
        adapter.RegisterCallbacks(BindItem);
        adapter.RegisterItemTemplateCallback(index => _items[index].TemplateId);
        adapter.Init(_items.Count);
    }

    private void OnDisable() => _view.ClearCallbacks();

    private void BindItem(VirtualItemCell holder, int index)
    {
        holder.GetComponentInChildren<Text>().text = _items[index].Title;
    }

    public void Add(string title, int templateId)
    {
        _items.Add((title, templateId));
        _view.RefreshItems(_items.Count);
    }
}
```

`RegisterCallbacks` 只有 `bindItem` 必填。回调在 `Init` 或下一次重建时生效，已绑定的实例用原回调回收。非活动适配器在启用时首次绑定条目。初始化后的数量变化用 `RefreshItems` 更新。

`Default Item Size` 的 X 用于横向列表的宽度，Y 用于纵向列表、网格和树的高度。有效的条目估算可覆盖组件默认值，实测尺寸会更新布局缓存。代码可通过 `DefaultItemSize` 修改默认值，并调用 `RefreshItems()` 应用。

`Init` 校验组件层级、滚动条、条目模板和布局参数。视口留空时使用适配器自身的 `RectTransform`，容器留空时创建 `Content`。每次建表都校验模板 ID，错误会报告对应的显示索引。整表刷新失败时会回收已绑定的条目，修正数据或回调后可再次调用 `RefreshItems`。

所有索引对应业务当前显示顺序，每次绑定都应覆盖可变 UI 状态。模板、预估尺寸和深度回调会按当前数量同步读取，期间应保持显示序列稳定。

| 回调 | 用途 |
| --- | --- |
| `bindItem(holder, index)` | 从业务集合按索引取数据，更新池化单元格的 UI 和交互 |
| `RegisterItemTemplateCallback(index => ...)` | 按显示索引选择模板。优先于 `getTemplateId`。省略两种选择回调时使用 ID `0` |
| `getEstimatedSize(index)` | 返回初始尺寸。省略回调或主轴尺寸无效时使用列表的 `Default Item Size` |
| `measureItem(holder, index)` | 实测主轴尺寸，可用 `measureItem: (holder, index) => holder.GetPreferredSize()` 读取自动布局结果。固定尺寸场景可省略 |
| `recycleItem(holder, index)` | 解绑业务监听、释放业务临时资源 |
| `getDepth(index)` | 树模式中的缩进深度，省略时使用 `0` |

## 点击、刷新和滚动回调

以下回调可单独注册，每次注册替换同类回调。传入 `null` 可单独注销，`ClearCallbacks()` 清理全部回调。

| 注册方法 | 触发时机 |
| --- | --- |
| `RegisterItemClickCallback((holder, index) => ...)` | 左键或触摸点击一个已绑定的在屏条目。单元格根或子节点需有可射线命中的 Graphic。自定义 `OnPointerClick` 需调用基类才能转发点击 |
| `RegisterRefreshRequestedCallback(() => ...)` | 业务或 UI 调用 `RequestRefresh()`。业务可异步加载数据，随后调用 `RefreshItems`。省略回调时直接刷新当前列表 |
| `RegisterListRefreshedCallback(count => ...)` | 全量 `RefreshItems` 完成重排和在屏绑定，参数是刷新后的条目数 |
| `RegisterItemRefreshedCallback(index => ...)` | `RefreshItem(index)` 应用完成 |
| `RegisterScrollStartedCallback(position => ...)` | 开始拖拽、开始平滑滚动或应用一次即时滚动 |
| `RegisterScrollChangedCallback(position => ...)` | 滚动位置变化并完成可见项更新 |
| `RegisterScrollEndedCallback(position => ...)` | 该次滚动停止，包括惯性、回弹和吸附结束后的停止位置 |

滚动回调和 `ScrollPosition` 使用 UGUI 布局单位，即时滚动按开始、变化、结束顺序回调。滚动条的值变更逐次应用为即时位置更新。

- `RefreshItems()` 使用当前条目数刷新，保留当前布局偏移。
- `RefreshItems(count, anchorIndex, offsetWithinAnchor)` 用于更新数量、顺序、模板或树深度。业务可提供新的锚点索引和偏移。
- `RefreshItem(index)` 更新指定条目的 UI。条目在屏时只重新绑定它的实例，离屏时等再次入屏才绑定。固定尺寸由预估尺寸回调更新，动态尺寸重新排队测量。模板或树深度改变时使用全量刷新。
- `MarkItemDirty(index)` 保留已绑定的 UI，只安排尺寸重测。

初始化和整表刷新会先同步视口布局，再设置条目矩形并绑定。默认每次刷新最多同步测量并重排四轮。剩余条目和滚动中新入屏的条目进入延迟队列。`Measure Delay Frames` 大于 `1` 或关闭 `Measure After Canvas Refresh` 时，刷新也走延迟测量。

## 排序和树展开

排序或展开树节点前，可用 `TryGetViewportAnchor(out oldIndex, out offset)` 记录当前节点。修改显示序列后，用该节点的新索引调用 `RefreshItems(newCount, newIndex, offset)`。节点被折叠时，可以改用它的父节点。省略锚点时按新的滚动范围约束当前偏移。

## 滚动与布局设置

- `ScrollToIndex(index)` 将指定条目的前沿对齐到视口起点，索引从 `0` 开始。循环列表选择最近一轮。若实测尺寸在动画期间改变了目标位置，控制器会更新终点。
- `ScrollToPosition(position)` 跳到内容坐标，纵向取 `y`，横向取 `x`。有限列表将目标约束在滚动范围内，循环列表允许以当前轮起点为基准传入跨轮偏移。
- 两个跳转接口默认使用 Inspector 的平滑滚动设置。可传入 `smooth` 覆盖本次调用。`SetScroll(origin)` 和 `AddScroll(delta)` 仍可用于即时设置偏移。
- 传入 `precise: true` 时，先用估算或缓存尺寸即时定位，再随在屏测量修正。设置 `smooth: true` 会平滑过渡到修正后的位置。坐标跳转以指定的布局坐标为目标，有限列表会随滚动范围变化重新约束该坐标。未入屏的条目仍使用估算尺寸。
- 索引跳转可传 `viewportIndex` 指定目标条目在视口内的位次，`0` 为第一项。空间不足、列表边界或网格整行布局限制时，优先保证目标可见。也可用 `ScrollIndexAlignment.Top` 或 `Bottom` 对齐到首尾边缘，横向对应左、右。
- 传入 `duration` 和 `EaseCurve` 可设置单次跳转的动画，时长用非缩放秒数，传 `0` 即时定位。可传 `onComplete` 接收最终偏移。回调在定位和精确修正结束后执行一次，即时跳转也可能在方法返回前完成。新跳转、拖拽、直接设置偏移、重建或禁用列表会中断当前请求，且不会调用其完成回调。
- `SetLoopScrolling(enabled)` 切换双向循环。较短的轮次可能同时显示同一索引的多个实例，绑定和回收按实例调用。
- `CurrentSettings` 和 `ApplyScrollSettings(settings)` 可读写惯性、回弹、吸附和补间配置。
- 手动组装时，可把适配器挂到列表根节点，并引用带裁剪和射线目标的子节点 `Viewport`。将 `Content` 放在 `Viewport` 下。适配器也可直接挂在 `Viewport` 上，此时视口引用可留空。场景需要 Canvas、GraphicRaycaster 和 EventSystem。列表会将视口坐标换算到 `Content`，支持偏移、旋转和缩放。两者之间的复合变换不能产生斜切。
- 右键创建的列表节点继承所选父节点的 Layer。业务条目 Prefab 使用自身的 Layer。
- 条目根节点的位置、旋转、缩放和尺寸由列表设置。视觉变换和驱动尺寸的自动布局可放在条目内部。视口次轴尺寸变化时，列表会按新的尺寸重排并保留首个在屏条目的锚点，再按滚动范围约束。

```csharp
adapter.ScrollToIndex(5, smooth: false);
adapter.RefreshItem(5);
adapter.ScrollToPosition(new Vector2(0f, 240f), smooth: true);

adapter.ScrollToIndex(20, smooth: true, precise: true, viewportIndex: 2);
adapter.ScrollToIndex(20, ScrollIndexAlignment.Bottom, precise: true);
adapter.ScrollToPosition(new Vector2(0f, 600f), smooth: true, precise: true);

adapter.ScrollToIndex(20, 0.35f, EaseCurve.EaseOutCubic,
    position => Debug.Log(position), precise: true, viewportIndex: 2);
adapter.ScrollToIndex(20, ScrollIndexAlignment.Bottom, 0.4f, EaseCurve.EaseOutQuart,
    position => Debug.Log(position), precise: true);
adapter.ScrollToPosition(new Vector2(0f, 600f), 0.5f, EaseCurve.Linear,
    position => Debug.Log(position));
```

从旧版迁移时，按布局将场景或 Prefab 上的 `VirtualScrollView` 换成对应适配器，并重新绑定视口、容器、滚动条和模板。原 `RegisterCallbacks(count, callbacks)` 拆成直接注册委托和 `Init(count)` 两步。业务排序或树展开后继续用 `RefreshItems` 通知视图。

## 许可证

Dynamic Virtual List 采用 [MIT 许可证](LICENSE.md)。
