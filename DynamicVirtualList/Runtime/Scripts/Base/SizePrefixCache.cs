using System;

namespace VirtualList
{
    /// <summary>用树状数组维护主轴累计长度和条目间距。</summary>
    public sealed class SizePrefixCache
    {
        private double[] _tree;
        private float[] _lengths;
        private int _count;
        private int _searchBit;
        private float _spacing;

        public int Count => _count;
        public float Spacing => _spacing;

        /// <summary>用相同的估算长度初始化每个条目。</summary>
        public void Init(int count, float spacing, float estimatedTotal)
            => Init(count, spacing, _ => estimatedTotal);

        internal void Init(int count, float spacing, Func<int, float> getLength)
        {
            _count = count;
            _spacing = NonNegative(spacing);
            if (_tree == null || _tree.Length <= count) _tree = new double[count + 1];
            else Array.Clear(_tree, 0, count + 1);
            if (_lengths == null || _lengths.Length < count) _lengths = new float[count];
            _searchBit = 1;
            while (_searchBit <= count / 2) _searchBit <<= 1;

            for (int index = 1; index <= count; index++)
            {
                float length = _lengths[index - 1] = Estimate(getLength(index - 1));
                _tree[index] += (double)length + _spacing;
                int parent = index + (index & -index);
                if (parent <= count) _tree[parent] += _tree[index];
            }
        }

        [Obsolete("SetLength updates the cache directly; marking it dirty is unnecessary.")]
        public void MarkDirty(int index) { }

        /// <summary>用实测长度更新对应条目及受影响的前缀和。</summary>
        public void SetLength(int index, float length)
        {
            if (index < 0 || index >= _count || !IsValidLength(length)) return;
            double delta = (double)length - _lengths[index];
            _lengths[index] = length;
            for (int slot = index + 1; slot <= _count; slot += slot & -slot)
                _tree[slot] += delta;
        }

        /// <summary>前 count 个条目的累计长度，含最后一个条目之后的间距。</summary>
        public float Prefix(int count) => (float)PrefixLength(count);

        internal double PrefixLength(int count)
        {
            double sum = 0;
            for (int slot = Math.Min(count, _count); slot > 0; slot -= slot & -slot)
                sum += _tree[slot];
            return sum;
        }

        /// <summary>按主轴偏移查找条目。落在间距内时返回前一个条目，空序列返回 -1。</summary>
        public int IndexAtOffset(float givenOffset)
        {
            if (_count == 0) return -1;
            int index = 0;
            double sum = 0;
            for (int bit = _searchBit; bit != 0; bit >>= 1)
            {
                int next = index + bit;
                if (next > _count || (float)(sum + _tree[next]) > givenOffset) continue;
                sum += _tree[next];
                index = next;
            }
            return Math.Min(index, _count - 1);
        }

        /// <summary>含条目之间的间距，省略末尾间距和内边距。</summary>
        public float TotalLength => _count == 0 ? 0f : (float)(PrefixLength(_count) - _spacing);

        internal static bool IsValidLength(float value) => value > 0f && !float.IsInfinity(value);

        internal static float Estimate(float value) => IsValidLength(value) ? value : 32f;

        internal static float NonNegative(float value) => IsValidLength(value) ? value : 0f;

        public void Clear()
        {
            _count = 0;
            _spacing = 0f;
            _tree = null;
            _lengths = null;
        }
    }
}
