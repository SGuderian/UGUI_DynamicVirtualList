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

        /// <summary>保存业务回调，在下一次初始化或布局重建时生效。参数中的索引均为当前显示索引。</summary>
        /// <param name="bindItem">必填。绑定时覆盖复用单元格中的旧内容。</param>
        /// <param name="getTemplateId">选择单元格模板，省略时使用 ID 0。</param>
        /// <param name="getEstimatedSize">提供首次排版使用的估算尺寸。</param>
        /// <param name="measureItem">布局后读取单元格尺寸。固定尺寸条目可省略。</param>
        /// <param name="recycleItem">单元格回池前解绑业务监听，此时仍可读取原显示索引。</param>
        /// <param name="getDepth">提供树节点深度，省略时为 0。</param>
        /// <remarks>模板、估算尺寸和深度回调在建表时同步读取。读取期间应保持显示序列稳定。</remarks>
        void RegisterCallbacks(Action<VirtualItemCell, int> bindItem,
            Func<int, int> getTemplateId = null, Func<int, Vector2> getEstimatedSize = null,
            Func<VirtualItemCell, int, Vector2> measureItem = null,
            Action<VirtualItemCell, int> recycleItem = null, Func<int, int> getDepth = null);

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

        /// <summary>结束当前滚动并设置主轴偏移。纵向读取 y，横向读取 x。</summary>
        void SetScroll(Vector2 origin);

        /// <summary>在当前主轴偏移上增加位移，正值朝显示序列末端。</summary>
        void AddScroll(Vector2 delta);

        void SetLoopScrolling(bool enabled);
        void ApplyScrollSettings(ScrollSettings settings);
    }
}
