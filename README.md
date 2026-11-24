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
| `VerticalList.unity` | 500 条数据、动态高度、池化复用 |
| `HorizontalList.unity` | 120 条数据、动态宽度、横向滚动条 |
| `Grid.unity` | 600 条数据、四列、按行聚合高度 |
| `Tree.unity` | 190 个业务节点、点击展开折叠、在业务中重定位视口锚点 |

四个场景都有添加数据、反转显示顺序、跳到末尾和切换循环滚动的按钮。示例预制体在 `Examples/Prefabs`，回调和业务集合在 `Examples/Scripts/DemoDataBootstrap.cs`。
单击普通列表项可选择或取消选择，单击树分支可展开或折叠。场景顶部会显示刷新和滚动状态。

## 接入业务

1. 打开 **Tools → VirtualList → 生成列表向导**，选择正式列表和布局，绑定业务 Item Prefab。Prefab 资产的根节点需要有 `RectTransform` 和 `VirtualItemCell`，也可使用派生组件 `AutoLayoutCell`。
2. 在业务组件中引用生成的适配器。统一引用可以声明为 `VirtualListAdapterBase`，业务操作使用 `IVirtualListAdapter`。直接把方法或委托传给 `RegisterCallbacks`，准备好显示序列后再调用 `Init(itemCount)`。
3. 改变显示内容、索引顺序、模板 ID、深度或数量后，先更新业务集合，再调用 `RefreshItems`。已绑定 UI 单纯发生尺寸变化时调用 `MarkItemDirty(index)`。
4. 业务退出或释放数据前调用 `ClearCallbacks()`。在回收回调中移除业务为该单元格注册的监听。单元格回收期间仍可读取旧索引。重新启用业务时，再注册并初始化。

```csharp
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using VirtualList;

public sealed class InventoryList : MonoBehaviour
{
    [SerializeField] private VirtualListAdapterBase _view;
    private readonly List<string> _items = new List<string> { "Potion", "Key" };

    private void OnEnable()
    {
        IVirtualListAdapter adapter = _view;
        adapter.RegisterCallbacks(BindItem, getEstimatedSize: EstimateItem);
        adapter.Init(_items.Count);
    }

    private void OnDisable() => _view.ClearCallbacks();

    private Vector2 EstimateItem(int index) => new Vector2(160f, 48f);

    private void BindItem(VirtualItemCell holder, int index)
    {
        holder.GetComponentInChildren<Text>().text = _items[index];
    }

    public void Add(string title)
    {
        _items.Add(title);
        _view.RefreshItems(_items.Count);
    }
}
```

`RegisterCallbacks` 只有 `bindItem` 必填。回调在 `Init` 或下一次重建时生效，已绑定的实例用原回调回收。非活动适配器在启用时首次绑定条目。初始化后的数量变化用 `RefreshItems` 更新。

`Init` 校验组件层级、滚动条、条目模板和布局参数。视口留空时使用适配器自身的 `RectTransform`，容器留空时创建 `Content`。每次建表都校验模板 ID，错误会报告对应的显示索引。整表刷新失败时会回收已绑定的条目，修正数据或回调后可再次调用 `RefreshItems`。

所有索引对应业务当前显示顺序，每次绑定都应覆盖可变 UI 状态。模板、预估尺寸和深度回调会按当前数量同步读取，期间应保持显示序列稳定。

| 回调 | 用途 |
| --- | --- |
| `bindItem(holder, index)` | 从业务集合按索引取数据，更新池化单元格的 UI 和交互 |
| `getTemplateId(index)` | 选择模板，省略时使用 ID `0` |
| `getEstimatedSize(index)` | 返回初始尺寸。主轴缺省或尺寸无效时用 `32` |
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

- `ScrollToIndex(index)` 根据 Inspector 的平滑滚动配置移动到该索引。动画期间如果实测尺寸改变了目标坐标，控制器会更新终点。
- `SetScroll(origin)` 和 `AddScroll(delta)` 以 UGUI 布局单位控制偏移。纵向用 `y`，横向用 `x`，正值朝后续条目。
- `SetLoopScrolling(enabled)` 切换双向循环。较短的轮次可能同时显示同一索引的多个实例，绑定和回收按实例调用。
- `CurrentSettings` 和 `ApplyScrollSettings(settings)` 可读写惯性、回弹、吸附和补间配置。
- 手动组装时，将控制器放在带裁剪和射线目标的 `Viewport` 上，`Content` 作为其子节点。场景需要 Canvas、GraphicRaycaster 和 EventSystem。列表会将视口坐标换算到 `Content`，支持偏移、旋转和缩放。两者之间的复合变换不能产生斜切。
- 向导生成的节点和示例条目继承所选父节点的 Layer。业务 Prefab 的 Layer 由 Prefab 自身决定。
- 条目根节点的位置、旋转、缩放和尺寸由列表设置。视觉变换和驱动尺寸的自动布局可放在条目内部。视口次轴尺寸变化时，列表会按新的尺寸重排并保留首个在屏条目的锚点，再按滚动范围约束。

从旧版迁移时，按布局将场景或 Prefab 上的 `VirtualScrollView` 换成对应适配器，并重新绑定视口、容器、滚动条和模板。原 `RegisterCallbacks(count, callbacks)` 拆成直接注册委托和 `Init(count)` 两步。业务排序或树展开后继续用 `RefreshItems` 通知视图。
