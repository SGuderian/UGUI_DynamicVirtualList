using System;
using UnityEngine;

namespace VirtualList
{
    /// <summary>虚拟列表的业务接口。数据、显示顺序和树的展开状态由调用方维护。</summary>
    public interface IVirtualListAdapter
    {
        /// <summary>是否已完成初始化。非活动组件会在启用后首次绑定条目。</summary>
        bool IsInitialized { get; }

        /// <summary>当前显示序列的总条目数，含离屏项。</summary>
        int ItemCount { get; }
        VirtualLayoutMode LayoutMode { get; }
        bool LoopScrolling { get; }

        /// <summary>内容左上角到视口左上角的滚动偏移，单位为 UGUI 布局单位。循环模式下为当前轮内的偏移。</summary>
        Vector2 ScrollPosition { get; }
        ScrollSettings CurrentSettings { get; }

        /// <summary>缺少有效条目估算时使用的尺寸。两轴均须为有限正数，修改后在下一次整表布局时生效。</summary>
        Vector2 DefaultItemSize { get; set; }

        /// <summary>保存业务回调，在下一次初始化或布局重建时生效。参数中的索引均为当前显示索引。</summary>
        /// <param name="bindItem">必填。绑定时覆盖复用单元格中的旧内容。</param>
        /// <param name="getTemplateId">选择单元格模板。没有独立或内联选择回调时使用 ID 0。</param>
        /// <param name="getEstimatedSize">提供首次排版使用的估算尺寸。主轴尺寸无效或省略时使用 <see cref="DefaultItemSize"/>。</param>
        /// <param name="measureItem">布局后读取单元格尺寸。固定尺寸条目可省略。</param>
        /// <param name="recycleItem">单元格回池前解绑业务监听，此时仍可读取原显示索引。</param>
        /// <param name="getDepth">提供树节点深度，省略时为 0。</param>
        /// <remarks>模板、估算尺寸和深度回调在建表时同步读取。读取期间应保持显示序列稳定。</remarks>
        void RegisterCallbacks(Action<VirtualItemCell, int> bindItem,
            Func<int, int> getTemplateId = null, Func<int, Vector2> getEstimatedSize = null,
            Func<VirtualItemCell, int, Vector2> measureItem = null,
            Action<VirtualItemCell, int> recycleItem = null, Func<int, int> getDepth = null);

        /// <summary>按显示索引返回 Inspector 中配置的模板 ID，在初始化或下一次整表刷新时生效。</summary>
        /// <remarks>此回调优先于 <see cref="RegisterCallbacks"/> 的模板选择。传 null 恢复内联选择或 ID 0。</remarks>
        void RegisterItemTemplateCallback(Func<int, int> callback);

        /// <summary>注册在屏条目的点击回调，收到单元格和当前显示索引。传 null 注销。</summary>
        void RegisterItemClickCallback(Action<VirtualItemCell, int> callback);

        /// <summary>处理 <see cref="RequestRefresh"/>。由业务更新数据后调用刷新接口。传 null 恢复默认刷新。</summary>
        void RegisterRefreshRequestedCallback(Action callback);

        /// <summary>整表刷新应用后收到新的条目总数。传 null 注销。</summary>
        void RegisterListRefreshedCallback(Action<int> callback);

        /// <summary>单条刷新应用后收到该条目的显示索引。传 null 注销。</summary>
        void RegisterItemRefreshedCallback(Action<int> callback);

        /// <summary>收到滚动开始时的偏移。传 null 注销。</summary>
        void RegisterScrollStartedCallback(Action<Vector2> callback);

        /// <summary>偏移变化并更新在屏条目后，收到当前偏移。传 null 注销。</summary>
        void RegisterScrollChangedCallback(Action<Vector2> callback);

        /// <summary>拖拽、惯性、回弹或吸附全部结束后，收到停止位置。滚动中途被取消时也会结束本次通知。传 null 注销。</summary>
        void RegisterScrollEndedCallback(Action<Vector2> callback);

        /// <summary>校验配置并初始化列表。重复调用时，使用 <see cref="RefreshItems(int, int, float)"/> 更新数量。</summary>
        /// <param name="itemCount">初始显示条目数，可以为 0。</param>
        /// <exception cref="ArgumentOutOfRangeException">条目数小于 0。</exception>
        /// <exception cref="InvalidOperationException">缺少绑定回调或组件配置无效。</exception>
        void Init(int itemCount = 0);

        /// <summary>回收在屏条目，注销业务回调和交互回调。再次使用时需重新注册并初始化。</summary>
        void ClearCallbacks();

        /// <summary>向业务请求刷新。未注册请求回调时，按当前条目数刷新列表。</summary>
        void RequestRefresh();

        /// <summary>按当前条目数重建布局，保留偏移并按新滚动范围约束。</summary>
        void RefreshItems();

        /// <summary>业务更新数量、顺序、模板或树深度后，重建布局并绑定在屏条目。</summary>
        /// <param name="itemCount">更新后的显示条目数。</param>
        /// <param name="anchorIndex">原锚点在新序列中的索引。传 -1 保留当前偏移。</param>
        /// <param name="offsetWithinAnchor">视口起点相对锚点前沿的主轴偏移。</param>
        void RefreshItems(int itemCount, int anchorIndex = -1, float offsetWithinAnchor = 0f);

        /// <summary>重新绑定指定条目的在屏实例并更新尺寸。离屏条目下次入屏时绑定。</summary>
        /// <remarks>模板或树深度变化时需重建整表。</remarks>
        void RefreshItem(int index);

        /// <summary>安排在屏条目重新测量尺寸，保留当前绑定的内容。</summary>
        void MarkItemDirty(int visibleIndex);

        /// <summary>获取首个在屏条目的显示索引和视口相对其前沿的偏移。没有可用锚点时返回 false。</summary>
        bool TryGetViewportAnchor(out int index, out float offsetWithinItem);

        /// <summary>按当前平滑滚动设置跳到指定显示索引。超出索引范围时不处理。</summary>
        void ScrollToIndex(int visibleIndex);

        /// <summary>将指定显示索引的条目前沿对齐到视口起点，允许本次调用指定是否平滑滚动。</summary>
        /// <remarks>索引从 0 开始。有限列表按滚动范围约束，循环列表选择最近一轮。</remarks>
        void ScrollToIndex(int visibleIndex, bool smooth);

        /// <summary>跳到指定条目。精确模式先按估算定位，再根据实测尺寸修正。</summary>
        /// <param name="visibleIndex">目标的显示索引，从 0 开始。</param>
        /// <param name="smooth">是否使用补间。精确模式中的估算跳转是即时的，后续修正可平滑过渡。</param>
        /// <param name="precise">是否等待在屏测量并修正目标位置。</param>
        /// <param name="viewportIndex">期望条目在视口内的位置，从 0 开始。-1 表示默认顶部对齐。</param>
        /// <remarks>视口空间、列表边界和网格整行布局会限制可达的位置。</remarks>
        void ScrollToIndex(int visibleIndex, bool smooth, bool precise, int viewportIndex = -1);

        /// <summary>跳到指定索引及视口位次，为本次跳转指定动画和完成回调。</summary>
        /// <param name="visibleIndex">目标索引，从 0 开始。</param>
        /// <param name="duration">补间时长，单位为秒，使用非缩放时间。0 表示即时应用。</param>
        /// <param name="curve">本次跳转使用的缓动曲线。</param>
        /// <param name="onComplete">成功定位后执行一次，收到最终滚动偏移。若触发滚动结束事件，则在该事件后调用。请求被忽略或中断时不调用。</param>
        /// <param name="precise">先即时跳到估算位置，再对实测修正播放动画。</param>
        /// <param name="viewportIndex">目标在视口内的位次，从 0 开始。-1 表示顶部对齐。</param>
        /// <exception cref="ArgumentOutOfRangeException">时长为负数或非有限值。</exception>
        void ScrollToIndex(int visibleIndex, float duration, EaseCurve curve, Action<Vector2> onComplete = null,
            bool precise = false, int viewportIndex = -1);

        /// <summary>按 Inspector 平滑设置，将条目对齐到视口顶部或底部。</summary>
        void ScrollToIndex(int visibleIndex, ScrollIndexAlignment alignment, bool precise = false);

        /// <summary>对齐条目到指定边缘，并指定是否平滑、是否按实测尺寸修正。</summary>
        void ScrollToIndex(int visibleIndex, ScrollIndexAlignment alignment, bool smooth, bool precise);

        /// <summary>将条目对齐到指定边缘，使用本次指定的动画，结束后报告最终偏移。</summary>
        /// <remarks>动画和回调规则同位次跳转。横向列表的顶部和底部分别对应左侧和右侧。</remarks>
        void ScrollToIndex(int visibleIndex, ScrollIndexAlignment alignment, float duration, EaseCurve curve,
            Action<Vector2> onComplete = null, bool precise = false);

        /// <summary>按当前平滑滚动设置跳到指定内容坐标。纵向读取 y，横向读取 x。</summary>
        void ScrollToPosition(Vector2 position);

        /// <summary>跳到指定内容坐标，允许本次调用指定是否平滑滚动。坐标使用 UGUI 布局单位。</summary>
        /// <remarks>有限列表约束到滚动范围，循环列表以当前轮的起点为基准，允许传入跨轮偏移。</remarks>
        void ScrollToPosition(Vector2 position, bool smooth);

        /// <summary>跳到指定内容坐标。精确模式会在测量结果改变滚动范围时重新约束目标坐标。</summary>
        void ScrollToPosition(Vector2 position, bool smooth, bool precise);

        /// <summary>跳到指定内容坐标，使用本次指定的动画，结束后报告最终偏移。</summary>
        /// <remarks>动画和回调规则同索引跳转。精确模式按实测后的滚动范围重新约束目标坐标。纵向读取 y，横向读取 x。</remarks>
        void ScrollToPosition(Vector2 position, float duration, EaseCurve curve, Action<Vector2> onComplete = null,
            bool precise = false);

        /// <summary>结束当前滚动并设置主轴偏移。纵向读取 y，横向读取 x。</summary>
        void SetScroll(Vector2 origin);

        /// <summary>在当前主轴偏移上增加位移，正值朝显示序列末端。</summary>
        void AddScroll(Vector2 delta);

        void SetLoopScrolling(bool enabled);
        void ApplyScrollSettings(ScrollSettings settings);
    }
}
